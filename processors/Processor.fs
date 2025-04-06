module Processor

open System.Threading
open ProcessorMessage

// Base processor functions.

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

      /// Handler function to process messages.
      handler: 'TMsg -> Async<unit>

      /// Handler function to clean up resources on shutdown.
      stopped: StopPriority -> StopChildren -> Async<unit> }

    static member empty = {
        name = ""
        handler = fun _ -> async { () }
        stopped = fun _ _ -> async { () } }

/// Start a simple processor with the given options.
let start options = 
    // Create a shutdown event to signal when the processor has shut down.
    let stopEvent = new ManualResetEvent(false)

    {
        stopEvent = stopEvent
        mailboxProcessor = 
            MailboxProcessor.Start(fun inbox -> async {
                let rec loop () = async {
                    match! inbox.Receive() with
                    | Process msg -> 
                        msg |> options.handler |> Async.RunSynchronously
                        return! loop ()
                    | Stop (Lowest, withChildren, _) when inbox.CurrentQueueLength = 0 ->                        
                            // If there are no items waiting in the queue, post a stop next message.
                            Stop (Next, withChildren, Some Lowest) |> inbox.Post
                            return! loop ()
                    | Stop (Lowest, withChildren, _) ->
                            // There are items remaining in the queue, so just post the message back to the end of the queue.
                            Stop (Lowest, withChildren, Some Lowest) |> inbox.Post
                            return! loop ()
                    | Stop (Next, withChildren, actual) -> 
                        printfn "Stopping %s Processor..." options.name
                        options.stopped (actual |> Option.defaultValue Next) withChildren |> Async.RunSynchronously
                        stopEvent.Set() |> ignore
                        printfn "%s Processor stopped." options.name                        
                        return ()
                }

                printfn "Starting %s Processor..." options.name
                return! loop ()
            })
    }

/// Dispatch a message to a processor.
let dispatch processor msg =
    processor.mailboxProcessor.Post(Process msg)

/// Stop a processor and wait for it to shut down.
let stop processor priority withChildren = async {
    Stop (priority, withChildren, None) |> processor.mailboxProcessor.Post
    processor.stopEvent.WaitOne() |> ignore
}

/// Start a round-robin processor with the given options.
/// This will start multiple instances of the processor and distribute messages in
/// a round-robin fashion between the various instances.
let startRoundRobin threadCount options =
    let shutdownEvent = new ManualResetEvent(false)

    {
        stopEvent = shutdownEvent

        mailboxProcessor = 
            MailboxProcessor.Start(fun inbox -> async {
                let counter = ref 0
                let workers = [ for i in 0..threadCount -> start { name=options.name; handler=options.handler; stopped=(fun _ _ -> async { () }) } ]

                let rec loop () = async {
                    match! inbox.Receive() with
                    | Process msg ->
                        let index = counter.Value % workers.Length
                        counter.Value <- counter.Value + 1
                        msg |> dispatch workers[index]
                        return! loop ()
                    | Stop (Lowest, withChildren, actual) when inbox.CurrentQueueLength = 0 ->
                        let remaining =  workers |> List.sumBy (fun w -> w.mailboxProcessor.CurrentQueueLength)
                        if remaining = 0 then
                            // There are no items waiting in the queue, so we can shut down immediately.
                            Stop (Next, withChildren, Some Lowest) |> inbox.Post
                        else
                            // Return the message to the end of the queue.
                            Stop (Lowest, withChildren, actual) |> inbox.Post
                    | Stop (Lowest, withChildren, actual) ->
                        // We still have remaining items to process, so return the message to the end of the queue.
                        Stop (Lowest, withChildren, actual) |> inbox.Post
                    | Stop (Next, withChildren, actual) ->
                        printfn "Stopping %d %s Processors..." threadCount options.name
                        
                        let actual = actual |> Option.defaultValue Next

                        // Shut down each worker and wait.
                        workers 
                        |> List.iter (fun worker -> stop worker actual withChildren |> Async.RunSynchronously)

                        // Call the shutdown handler for the main processor.
                        options.stopped actual withChildren |> Async.RunSynchronously

                        // Signal that the processor has shut down.
                        shutdownEvent.Set() |> ignore
                        printfn "Stopped %d %s Processors." threadCount options.name
                }

                printfn "Starting %d %s Processors..." threadCount options.name
                return! loop ()
            })
    }