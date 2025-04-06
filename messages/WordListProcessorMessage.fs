module WordListProcessorMessage

/// Message which can be handled by the LLM processor.
// Extra messages could be added, for example if we wanted to be able to
// retrieve a word list from a database or file instead.
type Message =
    /// Process a word list from a URL.
    | ProcessUrl of string * string option