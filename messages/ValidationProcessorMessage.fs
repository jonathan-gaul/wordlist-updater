module ValidationProcessorMessage

open Models

/// Message which can be handled by the validation processor.
type Message =
    /// Validate a word record.
    | Validate of WordRecord