using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Terrable
{

    class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            await Parser.Default.ParseArguments<Options>(args)
                .WithParsedAsync(RunAsync);
        }

        private static async Task<int> RunAsync(Options opts)
        {
            var services = new ServiceCollection();
            RegisterServices(services, opts);
            var sp = services.BuildServiceProvider();

            var logger = sp.GetRequiredService<ILogger<Program>>();

            logger.LogTrace("Starting to pull env");

            var target = new TerraformTarget
            {
                Version = opts.Target,
                Arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "amd64",
                    Architecture.X86 => "386",
                    Architecture.Arm => "arm",
                    Architecture.Arm64 => "arm64",
                    _ => throw new NotSupportedException("Your cpu architecture is not supported")
                },
                Platform = Environment.OSVersion.Platform switch
                {
                    _ when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => "windows",
                    _ when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => "linux",
                    _ when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => "darwin",
                    _ when RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) => "openbsd",
                    _ => throw new NotSupportedException("Your platform is unsupported :(")
                },
                Hash = opts.Hash
            };

            logger.LogTrace("Target created: {@target}", target);

            var app = sp.GetRequiredService<Terrable>();

            try
            {
                await app.RunAsync(target, opts.Force);
            }
            catch (Exception)
            {
                return 1;
            }

            return 0;
        }

        private static void RegisterServices(IServiceCollection services, Options opts)
        {
            var logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .MinimumLevel.Is(opts.Verbose ? LogEventLevel.Verbose : LogEventLevel.Information)
                .Enrich.FromLogContext()
                .CreateLogger();

            services.AddLogging(config =>
            {
                config.ClearProviders();
                config.AddSerilog(logger);
            });

            services.AddHttpClient();
            services.AddSingleton<Terrable>();
        }

        public class Options
        {
            [Option(
                shortName: 't',
                longName: "target",
                Required = true,
                HelpText = "The target version of terraform you want to swap to")]
            public string Target { get; set; }

            [Option(
                shortName: 'v',
                longName: "verbose",
                Required = false,
                HelpText = "Enable verbose logging")]
            public bool Verbose { get; set; }

            [Option(
                shortName: 'f',
                longName: "force",
                Required = false,
                HelpText = "Whether to fetch the given version from the web even if it exists in the cache")]
            public bool Force { get; set; }


            [Option(
                shortName: 'h',
                longName: "hash",
                Required = false,
                HelpText = "The hash to check the downloaded archive against")]
            public string Hash { get; set; }
        }
    }
}
