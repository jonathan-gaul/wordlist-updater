# WordList updater

## Requirements

Expects an existing MSSQL database with two tables:

- words:
	- word (nvarchar(50), primary key)
	- offensiveness (int)
	- commonness (int)
	- sentiment (int)
- word_types:
	- word (nvarchar(50), primary key)
	- type (nvarchar(50), primary key)

The following configuration options are also required (set by either environment variables or on the command line):

| Environment Variable | Command Line Argument  | Description |
|----------------------|------------------------|-------------|
| CHATGPT_API_KEY	   | --chatgpt-api-key		| API key for OpenAI ChatGPT. |
| LLM_WORD_LIST_SIZE   | --llm-word-list-size   | Number of words to include in a single request to the LLM. |
| DB_CONNECTION_STRING | --db-connection-string | Connection string for SQL server database. |
| DB_BATCH_SIZE		   | --db-batch-size		| Number of words to update in the database in a single query. |

## Description

Fetches the latest "alpha word list" from https://raw.githubusercontent.com/dwyl/english-words/master/words_alpha.txt and runs it through ChatGPT, generating a list of words with their offensiveness and commonness scores, "sentiment", and type of word. The script then updates the database with the new words and their scores, and adds type mappings.
