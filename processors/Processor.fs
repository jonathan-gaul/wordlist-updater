module Processor

open System.Threading
open ProcessorMessage
open System.Collections.Generic
open System

// Base processor functions.
let processorMap = Dictionary<Type, obj>()

/// Processor definition.
type Processor<'TMsg> = 
    { /// The mailbox processor instance for this processor.
      mailboxProcessor: MailboxProcessor<Message<'TMsg>>

      /// Event to signal when the processor has stopped.
      stopEvent: ManualResetEvent }

/// Startup configuration options for the processor.
type ProcessStartOptions<'TMsg> =
    { /// Name of the processor.
      name: string

      /// Handler function to process messages. (self -> message -> result)
      handler: Processor<'TMsg> -> 'TMsg -> Async<unit>

      /// Handler function to clean up resources on shutdown. (self -> priority -> withChildren -> result)
      stopped: Processor<'TMsg> -> StopPriority -> Async<unit>
      
      /// Register as a public target for messages
      register: bool}

    static member empty = {
        name = ""
        handler = fun _ _ -> async { () }
        stopped = fun _ _ -> async { () }
        register = true }

/// Start a simple processor with the given options.
let start<'TMsg> (options : ProcessStartOptions<'TMsg>) =
    // Create a shutdown event to signal when the processor has shut down.
    let stopEvent = new ManualResetEvent(false)

    let processor = 
        {
            stopEvent = stopEvent
            mailboxProcessor =
                MailboxProcessor.Start(fun inbox -> async {
                    let self = {
                        mailboxProcessor = inbox
                        stopEvent = stopEvent
                    }

                    let rec loop () = async {
                        match! inbox.Receive() with
                        | Process msg -> 
                            msg |> options.handler self |> Async.RunSynchronously
                            return! loop ()
                        | Stop (Lowest, _) when inbox.CurrentQueueLength = 0 ->                        
                                // If there are no items waiting in the queue, post a stop next message.
                                Stop (Next, Some Lowest) |> inbox.Post
                                return! loop ()
                        | Stop (Lowest, _) ->
                                // There are items remaining in the queue, so just post the message back to the end of the queue.
                                Stop (Lowest, Some Lowest) |> inbox.Post
                                return! loop ()
                        | Stop (Next, actual) -> 
                            printfn "Stopping %s Processor..." options.name
                            options.stopped self (actual |> Option.defaultValue Next) |> Async.RunSynchronously
                            stopEvent.Set() |> ignore
                            printfn "%s Processor stopped." options.name                        
                            return ()
                    }

                    printfn "Starting %s Processor..." options.name
                    return! loop ()
                })
        }

    if options.register then
        processorMap.[typeof<'TMsg>] <- processor

    processor


/// Send a message to a specific processor
let post processor msg =
    Process msg |> processor.mailboxProcessor.Post

/// Dispatch a message to the appropriate processor.
let dispatch<'TMsg> (msg : 'TMsg) =
    processorMap[typeof<'TMsg>] 
    :?> Processor<'TMsg>
    |> fun processor -> msg |> post processor

/// Stop a processor and wait for it to shut down.
let stop processor priority = async {
    Stop (priority, None) |> processor.mailboxProcessor.Post
    processor.stopEvent.WaitOne() |> ignore
}

let stopByMessageType priority messageType =
    match processorMap.TryGetValue(messageType) with
    | true, processor ->
        // Stop the processor and wait for it to shut down.
        let processor = processor :?> Processor<obj>
        stop processor priority
    | false, _ ->
        // No processor found for the given type.
        async { () }

/// Start a round-robin processor with the given options.
/// This will start multiple instances of the processor and distribute messages in
/// a round-robin fashion between the various instances.
let startRoundRobin<'TMsg> threadCount (options : ProcessStartOptions<'TMsg>) =
    let stopEvent = new ManualResetEvent(false)

    let processor = 
        {
            stopEvent = stopEvent

            mailboxProcessor = 
                MailboxProcessor.Start(fun inbox -> async {
                    let self = {
                        mailboxProcessor = inbox
                        stopEvent = stopEvent
                    }

                    let counter = ref 0
                    let workers = [ 
                        for i in 0..threadCount do
                            start { 
                                name = options.name
                                handler = options.handler
                                stopped = (fun _ _ -> async { () }) 
                                register = false
                            } 
                    ]

                    let rec loop () = async {
                        match! inbox.Receive() with
                        | Process msg ->
                            let index = counter.Value % workers.Length
                            counter.Value <- counter.Value + 1
                            msg |> post workers[index]
                            return! loop ()
                        | Stop (Lowest, actual) when inbox.CurrentQueueLength = 0 ->
                            let remaining =  workers |> List.sumBy (fun w -> w.mailboxProcessor.CurrentQueueLength)
                            if remaining = 0 then
                                // There are no items waiting in the queue, so we can shut down immediately.
                                Stop (Next, Some Lowest) |> inbox.Post
                            else
                                // Return the message to the end of the queue.
                                Stop (Lowest, actual) |> inbox.Post
                        | Stop (Lowest, actual) ->
                            // We still have remaining items to process, so return the message to the end of the queue.
                            Stop (Lowest, actual) |> inbox.Post
                        | Stop (Next, actual) ->
                            printfn "Stopping %d %s Processors..." threadCount options.name
                        
                            let actual = actual |> Option.defaultValue Next

                            // Shut down each worker and wait.
                            workers 
                            |> List.iter (fun worker -> stop worker actual |> Async.RunSynchronously)

                            // Call the shutdown handler for the main processor.
                            options.stopped self actual |> Async.RunSynchronously

                            // Signal that the processor has shut down.
                            stopEvent.Set() |> ignore
                            printfn "Stopped %d %s Processors." threadCount options.name
                    }

                    printfn "Starting %d %s Processors..." threadCount options.name
                    return! loop ()
                })
        }

    if options.register then
        processorMap.[typeof<'TMsg>] <- processor

    processor