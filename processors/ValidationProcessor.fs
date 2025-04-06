module ValidationProcessor

open Util

// ======================================================================
// Validation processor
// ----------------------------------------------------------------------
// - Receives score records (in CSV format).
// - Validates the records and their scores.
// - Sends valid words to the database processor for updating.
// ======================================================================

/// Message which can be handled by the validation processor.
type Message =
    /// Validate a word record.
    | Validate of string

/// Validation processor configuration options.
type Configuration = 
    { /// Number of threads to use for processing.
      threadCount : int }

    /// Default configuration for the validation processor.
    static member empty = {
        threadCount = 5
    }

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
                        DbProcessor.WordRecord.empty with
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
                    | x -> 
                        // send valid records for db update
                        DbProcessor.Update x |> Processor.dispatch dbProcessor
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