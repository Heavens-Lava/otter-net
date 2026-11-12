using System.Text;

namespace Otter.Cli;

internal static class TutorialCommand
{
    public static int Run(CommandLine line)
    {
        string[] raw = line.Raw;
        bool nonInteractive = raw.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "--non-interactive", StringComparison.OrdinalIgnoreCase));

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("===============================================================================");
        Console.WriteLine("                    WELCOME TO THE OTTER PROGRAMMING LANGUAGE                  ");
        Console.WriteLine("               First-Run Onboarding Tutorial & Interactive Guide               ");
        Console.WriteLine("===============================================================================");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Otter 1.0.1 is a clean, approachable, natural-syntax programming language");
        Console.WriteLine("designed for building CLI tools, background services, games, and web apps.");
        Console.WriteLine();

        string[] steps = new[]
        {
            "Step 1: Writing Your First Otter Program",
            "Step 2: Running Your Program from the Terminal",
            "Step 3: Searching for Reusable Library Components",
            "Step 4: Installing an Official Pack",
            "Step 5: Generating an Application from a Recipe",
            "Step 6: Understanding and Modifying Code with Developer Tooling",
            "Step 7: Running Tests and Fixing Errors"
        };

        for (int i = 0; i < steps.Length; i++)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"--- [{i + 1}/7] {steps[i]} ---");
            Console.ResetColor();

            switch (i + 1)
            {
                case 1:
                    ExplainStep1();
                    break;
                case 2:
                    ExplainStep2();
                    break;
                case 3:
                    ExplainStep3();
                    break;
                case 4:
                    ExplainStep4();
                    break;
                case 5:
                    ExplainStep5();
                    break;
                case 6:
                    ExplainStep6();
                    break;
                case 7:
                    ExplainStep7();
                    break;
            }

            Console.WriteLine();
            if (!nonInteractive && i < steps.Length - 1)
            {
                Console.Write("Press [Enter] to continue to the next step, or 'q' to quit: ");
                string? input = Console.ReadLine();
                if (string.Equals(input?.Trim(), "q", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Tutorial paused. Run 'otter tutorial' anytime to restart.");
                    return Cli.ExitSuccess;
                }
                Console.WriteLine();
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("===============================================================================");
        Console.WriteLine("   Tutorial Complete! You are now ready to build awesome software with Otter.  ");
        Console.WriteLine("===============================================================================");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Useful commands to remember:");
        Console.WriteLine("  otter doctor           - Verify your environment & library status");
        Console.WriteLine("  otter library list     - Browse all snippets, packs, blueprints & recipes");
        Console.WriteLine("  otter doc <keyword>    - View language reference and examples");
        Console.WriteLine("  otter compose propose  - AI-assisted feature assembly");
        Console.WriteLine();
        return Cli.ExitSuccess;
    }

    static void ExplainStep1()
    {
        Console.WriteLine("Otter uses readable natural English syntax with periods to close blocks.");
        Console.WriteLine("Property access uses 'property of object' (no dot syntax).");
        Console.WriteLine();
        Console.WriteLine("Example 'hello.ot':");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  to greet name");
        Console.WriteLine("      say \"Hello, \" plus name plus \"! Welcome to Otter.\"");
        Console.WriteLine("  .");
        Console.WriteLine();
        Console.WriteLine("  ask yourName with \"What is your name? \"");
        Console.WriteLine("  greet yourName");
        Console.ResetColor();
    }

    static void ExplainStep2()
    {
        Console.WriteLine("You can run single Otter files directly with zero setup:");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  $ otter hello.ot");
        Console.ResetColor();
        Console.WriteLine("Or validate without running:");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  $ otter check hello.ot");
        Console.ResetColor();
        Console.WriteLine("Or create a structured project:");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  $ otter new console my-app");
        Console.ResetColor();
    }

    static void ExplainStep3()
    {
        Console.WriteLine("Otter comes with an offline reusable library catalog:");
        Console.WriteLine("- 38 Micro-pattern Snippets");
        Console.WriteLine("- 5 Official Packs (data-table, json-utils, logger-core, game-entity, game-combat)");
        Console.WriteLine("- 4 Blueprints (etl-pipeline, game-loop, rest-api-router, service-worker)");
        Console.WriteLine("- 4 Complete Application Recipes");
        Console.WriteLine();
        Console.WriteLine("Search by natural keywords:");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  $ otter library search \"combat game\"");
        Console.WriteLine("  $ otter snippet search \"json parse\"");
        Console.ResetColor();
    }

    static void ExplainStep4()
    {
        Console.WriteLine("Packs are modular, versioned packages installed into your project:");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  $ otter pack add otter-official/data-table");
        Console.ResetColor();
        Console.WriteLine("This updates 'otter.lock' and makes the pack available offline:");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  use \"@otter/data-table\"");
        Console.WriteLine("  tbl is createTable with columnList");
        Console.ResetColor();
    }

    static void ExplainStep5()
    {
        Console.WriteLine("Recipes generate entire pre-wired applications with blueprints, packs, and tests:");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  $ otter recipe list");
        Console.WriteLine("  $ otter recipe apply otter-official/cli-utility --project ./my-cli");
        Console.ResetColor();
        Console.WriteLine("This scaffolds entry points, configuration, and unit tests ready to customize.");
    }

    static void ExplainStep6()
    {
        Console.WriteLine("Use native language tooling to understand, navigate, and edit code safely:");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  $ otter format src/main.ot         # Auto-format 4-space indentation");
        Console.WriteLine("  $ otter doc say                    # View syntax & examples for keyword");
        Console.WriteLine("  $ otter complete src/main.ot 10 5  # Autocomplete symbols at cursor");
        Console.WriteLine("  $ otter definition src/main.ot 5 2 # Jump to function/type definition");
        Console.WriteLine("  $ otter rename src/main.ot 5 2 foo # Safely rename symbol across file");
        Console.ResetColor();
    }

    static void ExplainStep7()
    {
        Console.WriteLine("Otter includes a built-in test runner. Run tests in any project:");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  $ otter test");
        Console.ResetColor();
        Console.WriteLine("Test files can be 'test.ot', 'tests/test.ot', '*_test.ot', or 'test_*.ot'.");
        Console.WriteLine("Assertion pattern in Otter 1.0.1:");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  if actual is not expected");
        Console.WriteLine("      say \"FAIL: expected\" expected \"got\" actual");
        Console.WriteLine("  otherwise");
        Console.WriteLine("      say \"PASS: test passed\"");
        Console.WriteLine("  .");
        Console.ResetColor();
    }
}
