module LlmProcessorMessage

/// Message which can be handled by the LLM processor.
type Message =
    /// Process a single word
    | Process of string     