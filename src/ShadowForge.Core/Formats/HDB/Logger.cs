using System;
using System.Collections.Generic;
using System.Text;

namespace ShadowForge.Formats.HDB;

public static class Logger
{
    // Set to false to silence debug logs when you are done fixing it
    public static bool IsDebugEnabled { get; set; } = true;

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