using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Code.Cil;
using EazyDevirt.Devirtualization;
using EazyDevirt.Devirtualization.Options;

namespace EazyDevirt;

internal static class Program
{
    private static readonly Version CurrentVersion = new("1.0.0");
    private static readonly Version CurrentEazVersion = new("2022.2.763.35371");

    private static async Task<int> Main(params string[] args)
    {
        // CLI must terminate deterministically in unattended TAB builds.
        var result = await BuildParser().InvokeAsync(args).ConfigureAwait(false);
        return result != 0 ? result : Environment.ExitCode;
    }

    private static void Run(DevirtualizationOptions options)
    {
        var ctx = new DevirtualizationContext(options);
        DevirtualizationContext.Instance = ctx;
        ctx.Console.ShowInfo(CurrentVersion, CurrentEazVersion);
        
        var devirtualizer = new Devirtualizer(ctx);
        if (!devirtualizer.Run())
        {
            ctx.Console.Error("Failed to devirtualize executable!");
            if (!options.SaveAnyway || options.TabAnalyzeOnly)
            {
                Environment.ExitCode = 1;
                return;
            }
        }

        // TAB analysis is a strict completeness gate, not merely a successful
        // traversal of the pipeline. Upstream's MethodDevirtualizer.Run() can
        // return true after individual VM methods failed to recover.
        // In this mode we must never report a partial result as usable.
        if (options.TabAnalyzeOnly)
        {
            var discovered = ctx.VMMethods?.Count ?? 0;
            var recovered = ctx.VMMethods?.Count(method => method.SuccessfullyDevirtualized) ?? 0;
            ctx.Console.Info($"TAB VM analysis: {discovered} discovered, {recovered} recovered.");
            if (discovered == 0 || recovered != discovered)
            {
                ctx.Console.Error("TAB VM analysis incomplete; no assembly was written.");
                Environment.ExitCode = 1;
                return;
            }

            ctx.Console.Success("TAB VM analysis complete; no assembly was written.");
            return;
        }

        ctx.Options.OutputPath.Create();
        var outputFilePath = Path.Combine(ctx.Options.OutputPath.FullName,
            Path.GetFileNameWithoutExtension(ctx.Options.Assembly.Name) +
            "-devirt" + ctx.Options.Assembly.Extension);

        var dnFactory =
            new DotNetDirectoryFactory(ctx.Options.PreserveAll
                ? MetadataBuilderFlags.PreserveAll
                : MetadataBuilderFlags.None)
            {
                MethodBodySerializer = new CilMethodBodySerializer
                {
                    ComputeMaxStackOnBuildOverride = false
                }
            };

        ctx.Module.Write(outputFilePath,
            new ManagedPEImageBuilder(
                dnFactory));
        ctx.Console.Success($"Saved file to {outputFilePath}");
    }
    
    private static Parser BuildParser()
    {
        var inputArgument = new Argument<FileInfo>("assembly", "Path to target assembly").ExistingOnly();
        inputArgument.AddValidator(result =>
        {
            var ext = result.GetValueForArgument(inputArgument).Extension;
            if (ext != ".exe" && ext != ".dll")
                result.ErrorMessage = $"Invalid file extension type '{ext}'. Valid extensions are '.exe' and '.dll'.";
        });
        
        var outputArgument = new Argument<DirectoryInfo>("output", "Path to output directory");
        outputArgument.SetDefaultValue(new DirectoryInfo("./eazydevirt-output"));
        
        var verbosityOption = new Option<int>(new[] { "--verbose", "-v" }, "Level of verbosity [1: Verbose, 2: Very Verbose, 3: Very Very Verbose]");
        verbosityOption.SetDefaultValue(0);
        verbosityOption.ArgumentHelpName = "verbosity";

        var preserveAllOption = new Option<bool>(new[] { "--preserve-all"}, "Preserves all metadata tokens");
        preserveAllOption.SetDefaultValue(false);

        var noVerifyOption = new Option<bool>(new[] { "--no-verify"}, "Don't verify labels or compute max stack for devirtualized methods");
        noVerifyOption.SetDefaultValue(false);
        
        // TODO: Implement this in code or remove this option
        var keepTypesOption = new Option<bool>(new[] { "--keep-types", "-kt"}, "Keeps obfuscator types");
        keepTypesOption.SetDefaultValue(false);
        
        var tabAnalyzeOption = new Option<bool>("--tab-analyze-only", "Run devirtualization analysis without writing an assembly");

        var saveAnywayOption = new Option<bool>(new[] { "--save-anyway"}, "Saves output of devirtualizer even if it fails");
        saveAnywayOption.SetDefaultValue(false);
        
        var onlySaveDevirtedOption = new Option<bool>(new[] { "--only-save-devirted"}, "Only saves successfully devirtualized methods (This option only matters if you use the save anyway option)");
        onlySaveDevirtedOption.SetDefaultValue(false);

        var requireDepsForGenerics = new Option<bool>(new[] { "--require-deps-for-generics"}, "Require dependencies when resolving generic methods for accuracy");
        requireDepsForGenerics.SetDefaultValue(true);

        
        var rootCommand = new RootCommand("is an open-source tool that automatically restores the original IL code " +
                                          "from an assembly virtualized with Eazfuscator.NET")
        {
            inputArgument,
            outputArgument,
            verbosityOption,
            preserveAllOption,
            noVerifyOption,
            keepTypesOption,
            saveAnywayOption,
            onlySaveDevirtedOption,
            requireDepsForGenerics,
            tabAnalyzeOption
        };

        rootCommand.SetHandler(Run,
            new DevirtualizationOptionsBinder(inputArgument, outputArgument, verbosityOption,
                preserveAllOption, noVerifyOption, keepTypesOption, saveAnywayOption, onlySaveDevirtedOption,
                requireDepsForGenerics, tabAnalyzeOption));
        
        return new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .UseHelp()
            .Build();
    }
}
