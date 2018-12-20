﻿using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using ArchitectNow.ApiStarter.Api.Configuration;
using ArchitectNow.ApiStarter.Api.Models.Validation;
using ArchitectNow.ApiStarter.Api.Services;
using ArchitectNow.ApiStarter.Common;
using ArchitectNow.ApiStarter.Common.Models.Options;
using ArchitectNow.ApiStarter.Common.Models.Security;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using AutofacSerilogIntegration;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Serilog;
using Serilog.Context;

namespace ArchitectNow.ApiStarter.Api
{
    public class Startup
    {
        private readonly IConfiguration _configuration;
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly ILogger<Startup> _logger;
        private IContainer _applicationContainer;

        public Startup(ILogger<Startup> logger, IConfiguration configuration, IHostingEnvironment hostingEnvironment)
        {
            _logger = logger;
            _configuration = configuration;
            _hostingEnvironment = hostingEnvironment;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            _logger.LogInformation($"{nameof(ConfigureServices)} starting...");

            services.AddOptions();
            services.ConfigureJwt(_configuration, ConfigureSecurityKey);

            services.ConfigureApi(new FluentValidationOptions {Enabled = false});

            services.AddAuthorization(options =>
            {
                options.AddPolicy("Default", builder => builder.RequireAuthenticatedUser().Build());
            });

            if (!_hostingEnvironment.IsDevelopment())
            {
                var key = _configuration["ApplicationInsights:InstrumentationKey"];
                services.AddApplicationInsightsTelemetry(key);
            }

            services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
            services.AddResponseCompression(options => { options.Providers.Add<GzipCompressionProvider>(); });

            services.AddOpenApiDocument(settings =>
            {
                settings.Title = "ArchitectNow API Workshop";
                settings.Description = "ASPNETCore API built as a demonstration during workshop";

                settings.SerializerSettings = new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    Converters = {new StringEnumConverter()}
                };

                settings.Version = Assembly.GetEntryAssembly().GetName().Version.ToString();
            });

            services.AddCors();

            //last
            _applicationContainer = services.CreateAutofacContainer((builder, serviceCollection) =>
            {
                builder.RegisterLogger();

                serviceCollection.AddAutoMapper(expression =>
                {
                    expression.ConstructServicesUsing(type => _applicationContainer.Resolve(type));
                });
            }, new CommonModule());

            // Create the IServiceProvider based on the container.
            var provider = new AutofacServiceProvider(_applicationContainer);

            _logger.LogInformation($"{nameof(ConfigureServices)} complete...");

            return provider;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(
            IApplicationBuilder builder,
            IApplicationLifetime appLifetime,
            IConfiguration configuration)
        {
            var logger = builder.ApplicationServices.GetService<ILogger<Startup>>();
            logger.LogInformation($"{nameof(Configure)} starting...");

            builder.UseFileServer();

            var uploadsPath = configuration["uploadsPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
            if (!Directory.Exists(uploadsPath)) Directory.CreateDirectory(uploadsPath);

            builder.UseStaticFiles();

            builder.UseCors(b => b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

            builder.UseAuthentication();

            builder.UseResponseCompression();

            builder.UseSwagger(settings => { settings.Path = "/docs/swagger.json"; });

            builder.UseSwaggerUi3(settings =>
            {
                settings.EnableTryItOut = true;
                settings.Path = "/docs";
                settings.DocumentPath = "/docs/swagger.json";
            });

            builder.Use(async (context, next) =>
            {
                LogContext.PushProperty("Environment", _hostingEnvironment.EnvironmentName);
                if (context.User.Identity.IsAuthenticated)
                {
                    var userInformation = context.User.GetUserInformation();
                    using (LogContext.PushProperty("User", userInformation))
                    {
                        await next.Invoke();
                    }
                }
                else
                {
                    using (LogContext.PushProperty("User", "anonymous"))
                    {
                        await next.Invoke();
                    }
                }
            });

            builder.UseMvc();

            appLifetime.ApplicationStopped.Register(Log.CloseAndFlush);

            try
            {
                _applicationContainer.Resolve<IMapper>().ConfigurationProvider.AssertConfigurationIsValid();
            }
            catch (AutoMapperConfigurationException exception)
            {
                if (_hostingEnvironment.IsDevelopment())
                {
                    logger.LogError(exception.Message);
                    throw;
                }

                logger.LogWarning(exception.Message);
            }

            logger.LogInformation($"{nameof(Configure)} complete...");
        }

        private JwtSigningKey ConfigureSecurityKey(JwtIssuerOptions issuerOptions)
        {
            var keyString = issuerOptions.Audience;
            var keyBytes = Encoding.Unicode.GetBytes(keyString);
            var signingKey = new JwtSigningKey(keyBytes);
            return signingKey;
        }
    }
}