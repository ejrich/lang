using System;
using System.Collections.Generic;
using System.Linq;

namespace ol;

public static class ErrorCodes
{
    public const int ArgumentsError = 1;
    public const int ParsingError = 2;
    public const int CompilationError = 3;
    public const int BuildError = 4;
    public const int LinkError = 5;
}

public class Error
{
    public string Message { get; init; }
    public int? FileIndex { get; set; }
    public uint Line { get; set; }
    public uint Column { get; set; }
}

public static class ErrorReporter
{
    public static List<Error> Errors { get; } = new();

    public static void Report(string message, int fileIndex, Token token)
    {
        Report(message, fileIndex, token.Line, token.Column);
    }

    public static void Report(string message, IAst ast)
    {
        if (ast != null)
        {
            Report(message, ast.FileIndex, ast.Line, ast.Column);
        }
        else
        {
            Report(message);
        }
    }

    public static void Report(string message)
    {
        Report(new Error {Message = message});
    }

    public static void Report(string message, int fileIndex, uint line, uint column)
    {
        Report(new Error {Message = message, FileIndex = fileIndex, Line = line, Column = column});
    }

    private static void Report(Error error)
    {
        if (!Errors.Any())
        {
            Messages.Submit(MessageType.TypeCheckFailed);
            Messages.Completed();
        }
        Errors.Add(error);
    }

    public static void ListErrorsAndExit(int errorCode)
    {
        if (Errors.Count > 0)
        {
            Console.WriteLine($"{Errors.Count} compilation error(s):\n");

            foreach (var error in Errors)
            {
                if (error.FileIndex.HasValue && error.FileIndex >= 0)
                {
                    Console.WriteLine($"{BuildSettings.FileName(error.FileIndex.Value)}: {error.Message} at line {error.Line}:{error.Column}");
                }
                else
                {
                    Console.WriteLine(error.Message);
                }
            }

            while (Messages.Intercepting);

            Environment.Exit(errorCode);
        }
    }
}
