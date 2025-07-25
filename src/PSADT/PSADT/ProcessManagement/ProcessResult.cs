﻿using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PSADT.ProcessManagement
{
    /// <summary>
    /// Represents the result of a process execution, including exit code and standard output/error.
    /// </summary>
    public sealed record ProcessResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessResult"/> struct.
        /// </summary>
        /// <param name="exitCode">The exit code of the process.</param>
        /// <param name="stdOut">The standard output of the process.</param>
        /// <param name="stdErr">The standard error output of the process.</param>
        public ProcessResult(int exitCode, ReadOnlyCollection<string> stdOut, ReadOnlyCollection<string> stdErr, ReadOnlyCollection<string> interleaved)
        {
            ExitCode = exitCode;
            StdOut = stdOut.SkipWhile(string.IsNullOrWhiteSpace).Reverse().SkipWhile(string.IsNullOrWhiteSpace).Reverse().ToList().AsReadOnly();
            StdErr = stdErr.SkipWhile(string.IsNullOrWhiteSpace).Reverse().SkipWhile(string.IsNullOrWhiteSpace).Reverse().ToList().AsReadOnly();
            Interleaved = interleaved.SkipWhile(string.IsNullOrWhiteSpace).Reverse().SkipWhile(string.IsNullOrWhiteSpace).Reverse().ToList().AsReadOnly();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessResult"/> struct.
        /// This is only here as sometimes we need to falsify a result in the module.
        /// </summary>
        /// <param name="exitCode"></param>
        public ProcessResult(int exitCode)
        {
            StdOut = StdErr = Interleaved = new ReadOnlyCollection<string>([]);
            ExitCode = exitCode;
        }

        /// <summary>
        /// Gets the exit code of the process, if the process had exited.
        /// </summary>
        public readonly int ExitCode;

        /// <summary>
        /// Gets the standard output of the process.
        /// </summary>
        public readonly IReadOnlyList<string> StdOut;

        /// <summary>
        /// Gets the standard error output of the process.
        /// </summary>
        public readonly IReadOnlyList<string> StdErr;

        /// <summary>
        /// Gets the combined standard output and error of the process.
        /// </summary>
        public readonly IReadOnlyList<string> Interleaved;
    }
}
