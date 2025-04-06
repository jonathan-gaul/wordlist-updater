module DbProcessorMessage

/// Represents a word with associated metadata.
type WordRecord = 
    { word: string 
      offensiveness: int
      commonness: int
      sentiment: int
      types: string array }

    /// Default value for a word record.
    static member empty = 
        { word = ""
          offensiveness = 0
          commonness = 0
          sentiment = 0
          types = [||] }

/// Message which can be handled by the database processor.
type Message =
    /// Update a word record in the database.
    | Update of WordRecord