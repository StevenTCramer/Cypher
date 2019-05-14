﻿// Cypher (c) by Tangram Inc
// 
// Cypher is licensed under a
// Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International License.
// 
// You should have received a copy of the license along with this
// work. If not, see <http://creativecommons.org/licenses/by-nc-nd/4.0/>.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Swagger;

namespace TangramCypher.ApplicationLayer
{
    public interface IHttpXy
    {

    }

    public class HttpXy : BackgroundService, IHttpXy
    {
        private class Startup
        {
            public Startup(IConfiguration configuration)
            {
                Configuration = configuration;
            }

            public IConfiguration Configuration { get; }

            public void ConfigureServices(IServiceCollection services)
            {
                services.AddMvc();
                services.AddSwaggerGen(options =>
                {
                    options.DescribeAllEnumsAsStrings();
                    options.SwaggerDoc("v1", new Info
                    {
                        Title = "Tangram Wallet Rest API",
                        Version = "v1",
                        Description = "Wallet API.",
                        TermsOfService = "https://tangrams.io/legal/",
                        Contact = new Contact { Email = "dev@getsneak.org", Url = "https://tangrams.io/about-tangram/team/" }
                    });
                });
                services.AddCors(options =>
                {
                    options.AddPolicy("CorsPolicy",
                        builder => builder.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials());
                });

                services.AddHttpContextAccessor();

                var logger = new LoggerFactory()
                                .AddFile("HttpXy.log")
                                .CreateLogger("HttpXy");

                services.Add(new ServiceDescriptor(typeof(ILogger),
                                                            provider => logger,
                                                            ServiceLifetime.Singleton));
                services.AddOptions();
            }

            public void Configure(IApplicationBuilder app, Microsoft.AspNetCore.Hosting.IHostingEnvironment env)
            {
                var pathBase = Configuration["PATH_BASE"];
                if (!string.IsNullOrEmpty(pathBase))
                {
                    app.UsePathBase(pathBase);
                }

                app.UseStaticFiles();
                app.UseCors("CorsPolicy");
                app.UseMvcWithDefaultRoute();

                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint($"{ (!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty) }/swagger/v1/swagger.json", "WalletRest.API V1");
                    c.OAuthClientId("walletrestswaggerui");
                    c.OAuthAppName("Wallet Rest Swagger UI");
                });
            }
        }

        public HttpXy(IConfiguration configuration)
        {

        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return BuildWebHost(null).RunAsync(stoppingToken);
        }

        public static IWebHost BuildWebHost(string[] args) =>
                  WebHost.CreateDefaultBuilder(args)
                         .UseContentRoot(Directory.GetCurrentDirectory())
                         .UseUrls("http://localhost:5001")
                         .UseStartup<Startup>()
                         .ConfigureLogging((hostingContext, builder) =>
                         {
                             builder.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                             builder.AddConsole();
                             builder.AddDebug();
                             builder.ClearProviders();
                         })
                         .Build();
    }
}
