# Personal Finance Tracker

A complete, standalone personal finance and ledger manager written in the Otter programming language.

## Features

- **Transaction Management**: Record income and expense entries with unique identifiers, timestamps, categories, amounts, and descriptions.
- **Categorization**: Categorize transactions across Housing, Groceries, Utilities, Entertainment, and Healthcare.
- **Analytics & Calculations**: Calculate total income, total expenditures, net savings, and category-level cost breakdowns.
- **Keyword & Category Search**: Query transaction history by text keywords or category names.
- **JSON Persistence**: Serialize the ledger to JSON on disk and load it back between sessions.
- **Automated Tests**: Unit test coverage validating financial arithmetic, record creation, and queries.

## Usage

Run the finance tracker demo:

```bash
otter run src/finance.ot
```

Run the automated test suite:

```bash
otter test .
```
