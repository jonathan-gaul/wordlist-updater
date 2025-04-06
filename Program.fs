
// ======================================================================
// Word List Updater
// ----------------------------------------------------------------------
// - Updates words in the word list database with scores from the LLM.
// ======================================================================

printfn "Starting updater..."

let options = Config.parseConfig

// Set up the processor chain.
let wordListProcessor = 
    DbProcessor.start { batchSize = options.dbBatchSize; connectionString = options.dbConnectionString.Value }
    |> ValidationProcessor.start ValidationProcessor.Configuration.empty
    |> LlmProcessor.start 
        { LlmProcessor.Configuration.empty with 
            batchSize = options.llmWordListSize
            apiKey = options.apiKey |> Option.defaultValue "" }
    |> WordListProcessor.start

// Send a filename to the Word List processor to download.
WordListProcessorMessage.ProcessUrl ("https://raw.githubusercontent.com/dwyl/english-words/master/words_alpha.txt", None)
|> Processor.dispatch wordListProcessor

// Send a shutdown message to the Word List processor and wait for it to shut down.
Processor.stop wordListProcessor true 
|> Async.RunSynchronously

printfn "All processing finished: Exiting."