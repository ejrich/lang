using System;
using System.Collections.Generic;

namespace Lang
{
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
        public int? FileIndex { get; init; }
        public uint Line { get; init; }
        public uint Column { get; init; }
    }

    public static class ErrorReporter
    {
        public static List<Error> Errors { get; } = new();

        public static void Report(string message, Token token)
        {
            Report(message, token.FileIndex, token.Line, token.Column);
        }

        public static void Report(string message, int fileIndex, uint line, uint column)
        {
            Errors.Add(new Error {Message = message, FileIndex = fileIndex, Line = line, Column = column});
        }

        public static void Report(string message, IAst ast = null)
        {
            Errors.Add(new Error {Message = message, FileIndex = ast?.FileIndex, Line = ast?.Line ?? 0, Column = ast?.Column ?? 0});
        }

        public static void ListErrorsAndExit(int errorCode, List<string> sourceFiles = null)
        {
            if (Errors.Count > 0)
            {
                if (sourceFiles == null)
                {
                    foreach (var error in Errors)
                    {
                        Console.WriteLine(error.Message);
                    }
                }
                else
                {
                }

                Environment.Exit(errorCode);
            }
        }
    }
}
