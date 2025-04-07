module ProcessorMessage

/// Determines priority of shutdown.
type StopPriority =
    /// Stop the processor when all messages in the queue 
    /// have been processed.
    | Lowest

    /// Stop the processor as soon as this message is received.
    | Next

/// Message which can be handled by a processor.
type Message<'TMsg> =
    /// Process a message.
    | Process of 'TMsg

    /// Shutdown the processor.
    | Stop of priority : StopPriority * previousPriority : StopPriority option