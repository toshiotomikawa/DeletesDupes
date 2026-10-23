using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DeleteDuplicates
{
    internal static class SafetyTests
    {
        private static string Root;
        private static int Count;
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern bool CreateHardLinkW(string name, string existing, IntPtr security);

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            Count++;
            Console.WriteLine("PASS: " + name);
        }
        private static CandidateImage File(string relative, byte[] data)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, data);
            return new CandidateImage { FullPath=path, RelativePath=relative,
                Depth=relative.Count(x=>x=='\\'), Length=data.Length, LastWriteTimeUtc=System.IO.File.GetLastWriteTimeUtc(path) };
        }
        private static DuplicateGroup Group(OperationLogger logger, params CandidateImage[] files)
        {
            var groups = new DuplicateDetector(logger).FindGroups(files.ToList());
            if (groups.Count!=1) throw new Exception("Expected one synthetic group");
            return groups[0];
        }
        public static int Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            try
            {
                if (args.Length!=1 || !Path.IsPathRooted(args[0])) return 2;
                Root=Path.Combine(args[0], "run-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
                Environment.SetEnvironmentVariable("TEMP", Root);
                Environment.SetEnvironmentVariable("TMP", Root);
                byte[] data=new byte[DuplicateDetector.BufferSize+17];
                for(int i=0;i<data.Length;i++) data[i]=(byte)(i%251);
                var a=File("equal-a.png",data); var z=File("equal-z.png",data);
                Check(DuplicateDetector.ByteEqual(a.FullPath,z.FullPath),"equal across read-block boundary");
                byte[] changed=(byte[])data.Clone();changed[changed.Length-1]^=1;
                var diff=File("different.png",changed);
                Check(!DuplicateDetector.ByteEqual(a.FullPath,diff.FullPath),"same length different last byte");
                var shortFile=File("short.png",new byte[]{1});
                Check(!DuplicateDetector.ByteEqual(a.FullPath,shortFile.FullPath),"different length");
                Check(DuplicateDetector.ComputeSha256(a.FullPath).SequenceEqual(DuplicateDetector.ComputeSha256(z.FullPath)),"SHA256 equality");
                var emptyA=File("empty-a.png",new byte[0]);var emptyB=File("empty-z.png",new byte[0]);
                Check(DuplicateDetector.ByteEqual(emptyA.FullPath,emptyB.FullPath),"empty files compare equal");
                var order=DuplicateDetector.Order(new[]{new CandidateImage{Depth=0,RelativePath="z"},new CandidateImage{Depth=2,RelativePath="a"},new CandidateImage{Depth=2,RelativePath="z"}});
                Check(order.Last().RelativePath=="z" && order.Last().Depth==2,"deepest then alphabetically last survivor");
                string why;
                foreach(string bad in new[]{".","C:relative","\\relative",@"\\server\share",@"\\?\C:\", "shell:Desktop"})
                    Check(RootValidator.Validate(bad,null,out why)==null,"reject non-local-absolute root: "+bad);
                Check(RootValidator.Validate(Path.GetPathRoot(Root),null,out why)==null,"reject entire drive root");
                Check(RootValidator.Validate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),null,out why)==null,"reject profile root");
                Check(PathHelper.IsProtectedDirectory(Path.Combine(Root,"$RECYCLE.BIN")),"exclude recycle bin directory");
                Check(PathHelper.IsProtectedDirectory(Path.Combine(Root,"System Volume Information")),"exclude volume metadata directory");
                var root=RootValidator.Validate(Root,null,out why);
                Check(root!=null,"accept synthetic local root");
                Check(!root.IsInside(Root+"-outside\\x.png"),"sibling prefix outside boundary");
                Check(!root.IsInside(Path.Combine(Root,"..","outside.png")),"dotdot outside boundary");
                Check(root.IsInside(a.FullPath),"child inside boundary");
                Check(new DeleteProgressSink(()=>true).PreDeleteItem(0,null)<0,"cancel before non-recycle operation");
                Check(new DeleteProgressSink(()=>true).PreDeleteItem(0x80,null)==0,"allow validated recycle request");
                Check(new DeleteProgressSink(()=>false).PreDeleteItem(0x80,null)<0,"cancel failed callback validation");
                Check(new DeleteProgressSink(null).PreDeleteItem(0x80,null)<0,"cancel absent validation");
                Check(new DeleteProgressSink(()=>{throw new IOException();}).PreDeleteItem(0x80,null)<0,"callback exception fails closed");
                using(var logger=OperationLogger.TryCreate())
                {
                    Check(logger!=null,"synthetic log available");
                    var detector=new DuplicateDetector(logger);
                    var groups=detector.FindGroups(new List<CandidateImage>{a,z,diff,shortFile});
                    Check(groups.Count==1 && groups[0].Ordered.Count==2,"group only exact duplicates");
                    var g=groups[0]; int calls=0;
                    Func<string,Func<bool>,RecycleResult> mock=(path,validate)=>{
                        calls++;
                        if(!validate())return RecycleResult.Failure(-1,"fixture validation failed");
                        return RecycleResult.Success("simulated only");
                    };
                    Program.ProcessGroup(g,logger,mock);
                    Check(calls==1,"one simulated recycle for one removable duplicate");
                    Check(System.IO.File.Exists(a.FullPath)&&System.IO.File.Exists(z.FullPath),"simulation preserves every fixture");
                    // A changed preferred survivor must not promote an older one.
                    var x=File("fallback-a.png",new byte[]{4,5});var y=File("fallback-b.png",new byte[]{4,5});var zz=File("fallback-z.png",new byte[]{4,5});
                    g=Group(logger,x,y,zz);System.IO.File.WriteAllBytes(zz.FullPath,new byte[]{8,9});calls=0;
                    Program.ProcessGroup(g,logger,mock);Check(calls==0,"changed survivor skips entire group without fallback");
                    var lockA=File("lock-a.png",new byte[]{10});var lockZ=File("lock-z.png",new byte[]{10});g=Group(logger,lockA,lockZ);calls=0;
                    using(var writer=new FileStream(lockZ.FullPath,FileMode.Open,FileAccess.Write,FileShare.ReadWrite))
                        Program.ProcessGroup(g,logger,mock);
                    Check(calls==0,"write-open survivor cannot bypass protective lock");
                    var raceA=File("race-a.png",new byte[]{11});var raceZ=File("race-z.png",new byte[]{11});g=Group(logger,raceA,raceZ);
                    System.IO.File.Move(raceA.FullPath,raceA.FullPath+".original");System.IO.File.WriteAllBytes(raceA.FullPath,new byte[]{11});
                    Check(!FileTreeScanner.StillEligible(raceA,out why),"same bytes replacement rejected by identity");
                    var hardA=File("hard-a.png",new byte[]{12});string hardZ=Path.Combine(Root,"hard-z.png");
                    Check(CreateHardLinkW(PathHelper.Extended(hardZ),PathHelper.Extended(hardA.FullPath),IntPtr.Zero),"create fixture hardlink");
                    var hz=new CandidateImage{FullPath=hardZ,RelativePath="hard-z.png",Length=1,LastWriteTimeUtc=System.IO.File.GetLastWriteTimeUtc(hardZ)};g=Group(logger,hardA,hz);calls=0;
                    Program.ProcessGroup(g,logger,mock);Check(calls==0,"hardlinked survivor skipped without unlocking");
                    var callbackA=File("callback-a.png",new byte[]{13});var callbackZ=File("callback-z.png",new byte[]{13});g=Group(logger,callbackA,callbackZ);
                    bool rejected=false;
                    Program.ProcessGroup(g,logger,(path,validate)=>{
                        System.IO.File.WriteAllBytes(path,new byte[]{14});
                        rejected=!validate();
                        return RecycleResult.Failure(-1,"simulated failure");
                    });
                    Check(rejected,"late candidate change rejected by pre-delete validation");
                    string scanRoot=Path.Combine(Root,"scan-only");Directory.CreateDirectory(scanRoot);
                    System.IO.File.WriteAllBytes(Path.Combine(scanRoot,"photo.PNG"),new byte[]{1});
                    System.IO.File.WriteAllBytes(Path.Combine(scanRoot,"ignore.txt"),new byte[]{1});
                    Directory.CreateDirectory(Path.Combine(scanRoot,"$Recycle.Bin"));
                    System.IO.File.WriteAllBytes(Path.Combine(scanRoot,"$Recycle.Bin","old.png"),new byte[]{1});
                    Directory.CreateDirectory(Path.Combine(scanRoot,"System Volume Information"));
                    System.IO.File.WriteAllBytes(Path.Combine(scanRoot,"System Volume Information","old.png"),new byte[]{1});
                    var scanValidated=RootValidator.Validate(scanRoot,null,out why);
                    var scanned=new FileTreeScanner(scanValidated,logger).Scan();
                    Check(scanned.Count==1 && scanned[0].RelativePath=="photo.PNG","scanner excludes bin metadata and unsupported extension");
                    var sink=new DeleteProgressSink(()=>true);sink.PostDeleteItem(0,null,0,null);
                    Check(!sink.RecycledItemPresent,"absent post-delete destination not success evidence");
                }
                Console.WriteLine("RESULT: "+Count+" passed; no Shell recycle, installer, registry edits or fixture deletion performed.");
                return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e.ToString());return 1;}
        }
    }
}
