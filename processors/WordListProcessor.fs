module WordListProcessor

open FSharp.Data
open Util

// ======================================================================
// Word List processor
// ----------------------------------------------------------------------
// - Fetches a word list.
// - Sends each word individually to the LLM processor for scoring.
// ======================================================================

/// Message which can be handled by the LLM processor.
// Extra messages could be added, for example if we wanted to be able to
// retrieve a word list from a database or file instead.
type Message =
    /// Process a word list from a URL.
    | ProcessUrl of string * string option

/// Start the word list processor waiting for messages to process.
/// The processor will output the word list to the given LLM processor.
let start llmProcessor =
    Processor.start {
        name = "Word List" 
        handler = fun msg -> async {
            match msg with
            | ProcessUrl (url, startWord) ->
                let response = Http.RequestStream url
                use reader = new System.IO.StreamReader(response.ResponseStream)

                let mutable started = startWord.IsNone
                let mutable count = 0

                printfn "Started streaming word list from %s" url
                while not reader.EndOfStream do
                    let line = reader.ReadLine() |> trim

                    if not started then
                        match startWord with 
                        | Some sw when line >= sw -> started <- true
                        | _ -> ()

                    if started && not (nullOrBlank line) then
                        LlmProcessor.Process line |> Processor.dispatch llmProcessor
                        count <- count + 1
                        if count % 10000 = 0 then
                            printfn "Processed %d word(s)..." count

                printfn "Finished streaming word list - %d words processed from %s" count url
        }
        shutdown = fun withChildren -> async {
            if withChildren then
                printfn "Stopping Word List Processor children..."
                Processor.stop llmProcessor true |> Async.RunSynchronously
                printfn "Stopped Word List Processor children."
        }
    }