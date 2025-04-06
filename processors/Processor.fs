module Processor

open System.Threading

// Base processor functions.

/// Message which can be handled by a processor.
type Message<'TMsg> =
    /// Process a message.
    | Process of 'TMsg

    /// Shutdown the processor.
    | Shutdown of bool

/// Processor definition.
type Processor<'TMsg> = 
    { /// The mailbox processor instance for this processor.
      mailboxProcessor: MailboxProcessor<Message<'TMsg>>

      /// Event to signal when the processor has shut down.
      shutdownEvent: ManualResetEvent }

/// Startup configuration options for the processor.
type ProcessStartOptions<'TMsg> =
    { /// Name of the processor.
      name: string

      /// Handler function to process messages.
      handler: 'TMsg -> Async<unit>

      /// Handler function to clean up resources on shutdown.
      shutdown: bool -> Async<unit> }

    static member empty = {
        name = ""
        handler = fun _ -> async { () }
        shutdown = fun _ -> async { () } }

/// Start a simple processor with the given options.
let start options = 
    // Create a shutdown event to signal when the processor has shut down.
    let shutdownEvent = new ManualResetEvent(false)

    {
        shutdownEvent = shutdownEvent
        mailboxProcessor = 
            MailboxProcessor.Start(fun inbox -> async {
                let rec loop () = async {
                    match! inbox.Receive() with
                    | Process msg -> 
                        msg |> options.handler |> Async.RunSynchronously
                        return! loop ()
                    | Shutdown withChildren -> 
                        printfn "Stopping %s Processor..." options.name
                        options.shutdown withChildren |> Async.RunSynchronously
                        shutdownEvent.Set() |> ignore
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
let stop processor withChildren = async {
    processor.mailboxProcessor.Post(Shutdown withChildren)
    processor.shutdownEvent.WaitOne() |> ignore
}

/// Start a round-robin processor with the given options.
/// This will start multiple instances of the processor and distribute messages in
/// a round-robin fashion between the various instances.
let startRoundRobin threadCount options =
    let shutdownEvent = new ManualResetEvent(false)

    {
        shutdownEvent = shutdownEvent

        mailboxProcessor = 
            MailboxProcessor.Start(fun inbox -> async {
                let counter = ref 0
                let workers = [ for i in 0..threadCount -> start { name=options.name; handler=options.handler; shutdown=(fun _ -> async { () }) } ]

                let rec loop () = async {
                    match! inbox.Receive() with
                    | Process msg ->
                        let index = counter.Value % workers.Length
                        counter.Value <- counter.Value + 1
                        msg |> dispatch workers[index]
                        return! loop ()
                    | Shutdown withChildren ->
                        printfn "Stopping %d %s Processors..." threadCount options.name
                        
                        // Shut down each worker and wait.
                        workers 
                        |> List.iter (fun worker -> stop worker withChildren |> Async.RunSynchronously)

                        // Call the shutdown handler for the main processor.
                        options.shutdown withChildren |> Async.RunSynchronously

                        // Signal that the processor has shut down.
                        shutdownEvent.Set() |> ignore
                        printfn "Stopped %d %s Processors." threadCount options.name
                }

                printfn "Starting %d %s Processors..." threadCount options.name
                return! loop ()
            })
    }