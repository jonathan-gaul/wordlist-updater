
open ProcessorMessage

// ======================================================================
// Word List Updater
// ----------------------------------------------------------------------
// - Updates words in the word list database with scores from the LLM.
// ======================================================================

printfn "Starting updater..."

let options = Config.parseConfig

// Set up processors.
let dbProcessor = DbProcessor.start { batchSize = options.dbBatchSize; connectionString = options.dbConnectionString.Value } 
let validationProcessor = ValidationProcessor.start ValidationProcessor.Configuration.empty 
let llmProcessor = LlmProcessor.start { LlmProcessor.Configuration.empty with batchSize = options.llmWordListSize; apiKey = options.apiKey |> Option.defaultValue "" }
let wordListProcessor = WordListProcessor.start

// Send a filename to the Word List processor to download.
WordListProcessorMessage.ProcessUrl ("https://raw.githubusercontent.com/dwyl/english-words/master/words_alpha.txt", None) |> Processor.dispatch

printfn "Waiting for processors to finish processing..."

// Wait for processors to finish processing.
Processor.stop wordListProcessor Lowest |> Async.RunSynchronously
Processor.stop llmProcessor Lowest |> Async.RunSynchronously
Processor.stop validationProcessor Lowest |> Async.RunSynchronously
Processor.stop dbProcessor Lowest |> Async.RunSynchronously

printfn "All processing finished: Exiting."