module ValidationProcessor

open Util
open ValidationProcessorMessage

// ======================================================================
// Validation processor
// ----------------------------------------------------------------------
// - Receives score records (in CSV format).
// - Validates the records and their scores.
// - Sends valid words to the database processor for updating.
// ======================================================================

/// Validation processor configuration options.
type Configuration = 
    { /// Number of threads to use for processing.
      threadCount : int }

    /// Default configuration for the validation processor.
    static member empty = {
        threadCount = 5
    }

let validTypes = Set.ofArray [|
    "noun"
    "verb"
    "adjective"
    "adverb"
    "pronoun"
    "preposition"
    "conjunction"
    "interjection"
    "article"
|]

/// Validation processor configuration options.
let start config dbProcessor =
    Processor.startRoundRobin config.threadCount {
        name = "Validation" 
        handler = fun msg -> async {
            match msg with
            | Validate record ->
                try
                    let items = record |> split ","
                    let word = { 
                        DbProcessorMessage.WordRecord.empty with
                            word = items.[0] |> trim
                            offensiveness = int items.[1]
                            commonness = int items.[2]
                            sentiment = int items.[3]
                            types = items.[4] |> split "/" |> List.map trim |> List.map lower |> Array.ofList
                    }

                    match word with 
                    | x when nullOrBlank x.word || 
                        x.offensiveness < 0 || 
                        x.offensiveness > 10 ||
                        x.commonness < 0 || 
                        x.commonness > 10 ||
                        x.sentiment < -10 || 
                        x.sentiment > 10 ->
                        printfn "Skipping due to invalid value in record: %s" record
                    | x when x.types |> Array.forall validTypes.Contains |> not ->
                        printfn "Skipping due to invalid type in record: %s" record
                    | x -> 
                        // send valid records for db update
                        DbProcessorMessage.Update x |> Processor.dispatch dbProcessor
                with
                | ex -> 
                    printfn "Skipping due to error in record: %s" record
                    printfn "Error: %s" ex.Message
        }
        shutdown = fun withChildren -> async {
            if withChildren then 
                printfn "Stopping Validation Processor children..."
                Processor.stop dbProcessor true |> Async.RunSynchronously
                printfn "Stopped Validation Processor children."
        }
    }