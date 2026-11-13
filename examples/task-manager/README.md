# Desktop Task Manager

A real-world task management application written in Otter 1.0.1, showcasing both terminal CLI and graphical desktop (HTML/WPF) interfaces.

## Features

- **Create, Read, Update, Delete (CRUD)**: Manage tasks with title, description, priority (High/Medium/Low), and status.
- **Filter by Status**: View Pending, InProgress, and Completed (Done) tasks.
- **Search**: Full-text search across titles and descriptions.
- **Persistence**: Save and restore task lists to and from disk in JSON format.
- **Dual Interface**:
  - `src/tasks.ot`: Command-line interface with formatted progress reporting and stats.
  - `src/tasks_gui.ot`: Desktop interface using Otter's native window and layout elements.

## Running

### CLI Runner
```bash
otter run src/tasks.ot
```

### Graphical Desktop Application
```bash
# Compile to self-contained desktop web page
otter web src/tasks_gui.ot -NoOpen

# Or run desktop mode (opens browser/window)
otter desktop src/tasks_gui.ot
```

### Running Tests
```bash
otter test
```
