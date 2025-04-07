module WordListProcessor

open FSharp.Data
open Util
open WordListProcessorMessage

// ======================================================================
// Word List processor
// ----------------------------------------------------------------------
// - Fetches a word list.
// - Sends each word individually to the LLM processor for scoring.
// ======================================================================

/// Start the word list processor waiting for messages to process.
/// The processor will output the word list to the given LLM processor.
let start =
    Processor.start {
        name = "Word List" 
        handler = fun self msg -> async {
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
                        LlmProcessorMessage.Process line |> Processor.dispatch
                        count <- count + 1
                        if count % 10000 = 0 then
                            printfn "Processed %d word(s)..." count

                printfn "Finished streaming word list - %d words processed from %s" count url
        }
        stopped = fun _ _ -> async { () }
        register = true
    }