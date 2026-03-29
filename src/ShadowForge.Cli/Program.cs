using System.CommandLine;
using ShadowForge.Cli.Commands;
using ShadowForge.Text;

EncodingSetup.EnsureRegistered();

var root = new RootCommand("ShadowForge — Blue Dragon modding toolkit");
root.Subcommands.Add(RpjCommands.Create());
root.Subcommands.Add(IpkCommands.Create());
root.Subcommands.Add(HdbCommands.Create());
root.Subcommands.Add(DdsCommands.Create());

return root.Parse(args).Invoke();
