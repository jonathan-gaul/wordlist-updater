module DbProcessorMessage

open Models

/// Message which can be handled by the database processor.
type Message =
    /// Update a word record in the database.
    | Update of WordRecord