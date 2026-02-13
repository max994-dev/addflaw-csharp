## addflaw-csharp

This repository contains the C# project **AddFlaw**, which uses **Helix Viewport** to visualize and experiment with 3D scenes while you explore and debug behavior in the "fan" component (and related features). The main goal of the project is to provide a small, focused codebase that is easy to break, inspect, and fix.

### Features

- **Helix Viewport integration**: Uses Helix/HelixToolkit 3D viewport controls for rendering.
- **Focused domain**: Code organized around a "fan" model so you can add, find, and fix bugs in one place.
- **C#/.NET solution**: Standard `.sln` and project layout so it works well with Visual Studio, Rider, and `dotnet` CLI.

### Getting started

- **Requirements**
  - .NET SDK (6.0 or later is usually fine)
  - A Windows machine (recommended, since Helix Viewport is typically used with WPF/WinUI)
  - An IDE such as Visual Studio, Rider, or VS Code with C# extensions

- **Clone and open**
  1. Clone the repo: `git clone <this-repo-url>`
  2. Open `AddFlaw.sln` in your preferred IDE.

- **Build and run (from CLI)**
  1. Navigate into the repo directory: `cd addflaw-csharp`
  2. Restore and build: `dotnet build`
  3. Run the primary project (adjust the project path if needed):  
     `dotnet run --project AddFlaw`

### Project structure

The exact layout may evolve, but the core pieces are:

- `AddFlaw.sln` – Solution file for the whole project.
- `AddFlaw/` – Main application code (entry point, UI, and fan logic).
- Additional subfolders (e.g., `Models`, `ViewModels`, `Views`, `Rendering`) for separating UI, logic, and 3D components.

### Typical workflows

- **Explore existing behavior**: Run the app, interact with the fan visualization, and observe current behavior and any obvious bugs.
- **Add new flaws intentionally**: Introduce edge cases or suspicious logic in the fan-related code to create debugging exercises.
- **Fix and document bugs**: Use this repo as a practice ground for learning debugging techniques in C#, WPF, and Helix Viewport scenarios.

### Contributing

Contributions that improve clarity, add small focused examples, or introduce new debugging exercises are welcome. Please:

1. Create a new branch for your changes.
2. Keep pull requests small and well-documented.
3. Include a short description of the bug or scenario you are adding or fixing.

### License

MIT License