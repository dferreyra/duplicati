﻿//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace Duplicati.UnitTest
{
    public class CommandLineOperationsTests : BasicSetupHelper
    {
        /// <summary>
        /// The log tag
        /// </summary>
        private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<CommandLineOperationsTests>();

        /// <summary>
        /// The folder that contains all the source data which the test is based on
        /// </summary>
        protected readonly string SOURCEFOLDER = Path.Combine(BASEFOLDER, "data");

        private readonly string zipFilename = "data.zip";
        private string zipFilepath => Path.Combine(BASEFOLDER, this.zipFilename);

        private readonly string zipAlternativeFilename = "data-alternative.zip";
        private string zipAlternativeFilepath => Path.Combine(BASEFOLDER, this.zipAlternativeFilename);

        protected virtual IEnumerable<string> SourceDataFolders
        {
            get
            {
                return
                    from x in Directory.EnumerateDirectories(SOURCEFOLDER)
                    orderby x
                    select x;
            }
        }

        public override void OneTimeSetUp()
        {
            base.OneTimeSetUp();

            if (!File.Exists($@"{BASEFOLDER}\{zipAlternativeFilename}"))
            {
                using (WebClient client = new WebClient())
                {
                    client.DownloadFile($"https://s3.amazonaws.com/duplicati-test-file-hosting/{this.zipFilename}",
                        this.zipFilepath);
                }

                System.IO.Compression.ZipFile.ExtractToDirectory(this.zipFilepath, BASEFOLDER);
            }
            else
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(this.zipAlternativeFilepath, BASEFOLDER);
            }
        }

        public override void OneTimeTearDown()
        {
            if (Directory.Exists(this.SOURCEFOLDER))
            {
                Directory.Delete(this.SOURCEFOLDER, true);
            }
            if (File.Exists(this.zipFilepath))
            {
                File.Delete(this.zipFilepath);
            }
        }

        [Test]
        [Category("BulkData")]
        [Category("BulkNormal")]
        public void RunCommands()
        {
            DoRunCommands(TARGETFOLDER);
        }

        [Test]
        [Category("BulkData")]
        [Category("BulkNoSize")]
        public void RunCommandsWithoutSize()
        {
            DoRunCommands(new SizeOmittingBackend().ProtocolKey + "://" + TARGETFOLDER);
        }

        private void DoRunCommands(string target)
        {
            var opts = from n in TestOptions select string.Format("--{0}=\"{1}\"", n.Key, n.Value);
            var backupargs = (new string[] { "backup", target, DATAFOLDER }.Union(opts)).ToArray();

            if (SourceDataFolders == null || SourceDataFolders.Count() < 3)
            {
                ProgressWriteLine($"ERROR: A minimum of 3 data folders are required in {SOURCEFOLDER}.");
                throw new Exception("Failed during initial minimum data folder check");
            }

            foreach (var n in SourceDataFolders)
            {
                var foldername = Path.GetFileName(n);
                var targetfolder = Path.Combine(DATAFOLDER, foldername);
                ProgressWriteLine("Adding folder {0} to source", foldername);

                Directory.Move(n, targetfolder);

                var size = Directory.EnumerateFiles(targetfolder, "*", SearchOption.AllDirectories).Select(x => new FileInfo(x).Length).Sum();

                ProgressWriteLine("Running backup with {0} data added ...", Duplicati.Library.Utility.Utility.FormatSizeString(size));
                using (new Library.Logging.Timer(LOGTAG, "BackupWithDataAdded", string.Format("Backup with {0} data added", Duplicati.Library.Utility.Utility.FormatSizeString(size))))
                    Duplicati.CommandLine.Program.RealMain(backupargs);

                ProgressWriteLine("Testing data ...");
                using (new Library.Logging.Timer(LOGTAG, "TestRemoteData", "Test remote data"))
                    if (Duplicati.CommandLine.Program.RealMain((new string[] { "test", target, "all" }.Union(opts)).ToArray()) != 0)
                        throw new Exception("Failed during remote verification");
            }

            ProgressWriteLine("Running unchanged backup ...");
            using (new Library.Logging.Timer(LOGTAG, "UnchangedBackup", "Unchanged backup"))
                Duplicati.CommandLine.Program.RealMain(backupargs);

            var datafolders = Directory.EnumerateDirectories(DATAFOLDER);

            var f = datafolders.Skip(datafolders.Count() / 2).First();

            ProgressWriteLine("Renaming folder {0}", Path.GetFileName(f));
            Directory.Move(f, Path.Combine(Path.GetDirectoryName(f), Path.GetFileName(f) + "-renamed"));

            ProgressWriteLine("Running backup with renamed folder...");
            using (new Library.Logging.Timer(LOGTAG, "BackupWithRenamedFolder", "Backup with renamed folder"))
                Duplicati.CommandLine.Program.RealMain(backupargs);

            datafolders = Directory.EnumerateDirectories(DATAFOLDER);

            ProgressWriteLine("Deleting data");
            var rm1 = datafolders.First();
            var rm2 = datafolders.Skip(1).First();
            var rm3 = datafolders.Skip(2).First();

            Directory.Delete(rm1, true);
            Directory.Delete(rm2, true);
            var rmfiles = Directory.EnumerateFiles(rm3, "*", SearchOption.AllDirectories);
            foreach (var n in rmfiles.Take(rmfiles.Count() / 2))
                File.Delete(n);

            ProgressWriteLine("Running backup with deleted data...");
            using (new Library.Logging.Timer(LOGTAG, "BackupWithDeletedData", "Backup with deleted data"))
                Duplicati.CommandLine.Program.RealMain(backupargs);

            ProgressWriteLine("Testing the compare method ...");
            using (new Library.Logging.Timer(LOGTAG, "CompareMethod", "Compare method"))
                Duplicati.CommandLine.Program.RealMain((new string[] { "compare", target, "0", "1" }.Union(opts)).ToArray());

            for (var i = 0; i < 5; i++)
            {
                ProgressWriteLine("Running backup with changed logfile {0} of {1} ...", i + 1, 5);
                File.Copy(LOGFILE, Path.Combine(SOURCEFOLDER, Path.GetFileName(LOGFILE)), true);

                using (new Library.Logging.Timer(LOGTAG, "BackupWithLogfileChange", string.Format("Backup with logfilechange {0}", i + 1)))
                    Duplicati.CommandLine.Program.RealMain(backupargs);
            }

            ProgressWriteLine("Compacting data ...");
            using (new Library.Logging.Timer(LOGTAG, "Compacting", "Compacting"))
                Duplicati.CommandLine.Program.RealMain((new string[] { "compact", target, "--small-file-max-count=2" }.Union(opts)).ToArray());


            datafolders = Directory.EnumerateDirectories(DATAFOLDER);
            var rf = datafolders.Skip(datafolders.Count() - 2).First();

            ProgressWriteLine("Partial restore of {0} ...", Path.GetFileName(rf));
            using (new Library.Logging.Timer(LOGTAG, "PartialRestore", "Partial restore"))
                Duplicati.CommandLine.Program.RealMain((new string[] { "restore", target, rf + "*", "--restore-path=\"" + RESTOREFOLDER + "\"" }.Union(opts)).ToArray());

            ProgressWriteLine("Verifying partial restore ...");
            using (new Library.Logging.Timer(LOGTAG, "VerifiationOfPartialRestore", "Verification of partial restored files"))
                TestUtils.VerifyDir(rf, RESTOREFOLDER, true);

            Directory.Delete(RESTOREFOLDER, true);

            ProgressWriteLine("Partial restore of {0} without local db...", Path.GetFileName(rf));
            using (new Library.Logging.Timer(LOGTAG, "PartialRestoreWithoutLocalDb", "Partial restore without local db"))
                Duplicati.CommandLine.Program.RealMain((new string[] { "restore", target, rf + "*", "--restore-path=\"" + RESTOREFOLDER + "\"", "--no-local-db" }.Union(opts)).ToArray());

            ProgressWriteLine("Verifying partial restore ...");
            using (new Library.Logging.Timer(LOGTAG, "VerificationOfPartialRestore", "Verification of partial restored files"))
                TestUtils.VerifyDir(rf, RESTOREFOLDER, true);

            Directory.Delete(RESTOREFOLDER, true);

            ProgressWriteLine("Full restore ...");
            using (new Library.Logging.Timer(LOGTAG, "FullRestore", "Full restore"))
                Duplicati.CommandLine.Program.RealMain((new string[] { "restore", target, "*", "--restore-path=\"" + RESTOREFOLDER + "\"" }.Union(opts)).ToArray());

            ProgressWriteLine("Verifying full restore ...");
            using (new Library.Logging.Timer(LOGTAG, "VerificationOfFullRestore", "Verification of restored files"))
                foreach (var s in Directory.EnumerateDirectories(DATAFOLDER))
                    TestUtils.VerifyDir(s, Path.Combine(RESTOREFOLDER, Path.GetFileName(s)), true);

            Directory.Delete(RESTOREFOLDER, true);

            ProgressWriteLine("Full restore without local db...");
            using (new Library.Logging.Timer(LOGTAG, "FullRestoreWithoutDb", "Full restore without local db"))
                Duplicati.CommandLine.Program.RealMain((new string[] { "restore", target, "*", "--restore-path=\"" + RESTOREFOLDER + "\"", "--no-local-db" }.Union(opts)).ToArray());

            ProgressWriteLine("Verifying full restore ...");
            using (new Library.Logging.Timer(LOGTAG, "VerificationOfFullRestoreWithoutDb", "Verification of restored files"))
                foreach (var s in Directory.EnumerateDirectories(DATAFOLDER))
                    TestUtils.VerifyDir(s, Path.Combine(RESTOREFOLDER, Path.GetFileName(s)), true);

            ProgressWriteLine("Testing data ...");
            using (new Library.Logging.Timer(LOGTAG, "TestRemoteData", "Test remote data"))
                if (Duplicati.CommandLine.Program.RealMain((new string[] { "test", target, "all" }.Union(opts)).ToArray()) != 0)
                    throw new Exception("Failed during final remote verification");
        }
    }
}

