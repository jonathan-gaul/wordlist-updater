module Models

/// Represents a word with associated metadata.
type WordRecord = 
    { word: string 
      offensiveness: int option
      commonness: int option
      sentiment: int option
      types: string array }

    /// Default value for a word record.
    static member empty = 
        { word = ""
          offensiveness = None
          commonness = None
          sentiment = None
          types = [||] }