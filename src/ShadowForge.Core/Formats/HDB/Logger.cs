using System;
using System.Collections.Generic;
using System.Text;

namespace ShadowForge.Formats.HDB;

public static class Logger
{
    public static bool IsDebugEnabled { get; set; } = false;

    public static void Debug(string message)
    {
        if (IsDebugEnabled)
        {
            Console.WriteLine(message);
        }
    }

    public static void Warning(string message)
    {
        // Adding a little color makes warnings pop in the CLI
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"WARNING: {message}");
        Console.ResetColor();
    }
}