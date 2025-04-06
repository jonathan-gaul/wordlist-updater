module ProcessorMessage

/// Determines priority of shutdown.
type StopPriority =
    /// Stop the processor when all messages in the queue 
    /// have been processed.
    | Lowest

    /// Stop the processor as soon as this message is received.
    | Next

/// Determines whether a processor will send a stop message to 
/// other processors it regards as children.  
/// Note: Implementation of WithChildren is left to the processor
/// itself.
type StopChildren =
    /// Only stop this processor.
    | ThisOnly

    /// Stop other processors this processor regards as children.
    | WithChildren    

/// Message which can be handled by a processor.
type Message<'TMsg> =
    /// Process a message.
    | Process of 'TMsg

    /// Shutdown the processor.
    | Stop of priority : StopPriority * stopChildren : StopChildren * previousPriority : StopPriority option