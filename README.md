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

The following environment variables are also required:

- DB_CONNECTION_STRING: Connection string for SQL server database._
- CHATGPT_API_KEY: API key for ChatGPT

## Description

Fetches the latest "alpha word list" from https://raw.githubusercontent.com/dwyl/english-words/master/words_alpha.txt and runs it through ChatGPT, generating a list of words with their offensiveness and commonness scores, and type of word. The script then updates the database with the new words and their scores, and adds type mappings.
