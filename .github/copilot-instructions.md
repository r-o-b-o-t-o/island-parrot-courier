# Copilot Instructions

## Project Guidelines
- Codestyle preferences:
- Always use a line break and brackets after `if`/`for`/`while` even for single-line blocks
- No underscores for private variable naming, use lower camelCase
- Prefer primary constructors when constructor only assigns variables
- Use pattern matching: `if (x is not Type y) return;`
- Use coalesce expressions: `await db.FindAsync(id) ?? throw new InvalidOperationException()`
- When displaying an Archipelago slot, always use the alias instead of the name: `GetPlayerAlias()` over `GetPlayerName()`. However, do use the slot name when fetching a `Player` entity (e.g. `IGameRepository.GetPlayerBySlotAsync()`)
