using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Options;
using HstReceipts.Infrastructure.Auth;
using HstReceipts.Infrastructure.Data;
using HstReceipts.Infrastructure.Extraction;
using HstReceipts.Infrastructure.Export;
using HstReceipts.Infrastructure.Learning;
using HstReceipts.Infrastructure.Repositories;
using HstReceipts.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HstReceipts.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ReceiptProcessingOptions>(
            configuration.GetSection(ReceiptProcessingOptions.SectionName));
        services.Configure<DocumentIntelligenceOptions>(
            configuration.GetSection(DocumentIntelligenceOptions.SectionName));
        services.Configure<BlobStorageOptions>(
            configuration.GetSection(BlobStorageOptions.SectionName));
        services.Configure<ProcessingPipelineOptions>(
            configuration.GetSection(ProcessingPipelineOptions.SectionName));
        services.Configure<AiLearningOptions>(
            configuration.GetSection(AiLearningOptions.SectionName));
        services.Configure<SeedUsersOptions>(
            configuration.GetSection(SeedUsersOptions.SectionName));
        services.Configure<SmtpEmailOptions>(
            configuration.GetSection(SmtpEmailOptions.SectionName));

        var documentIntelligence = configuration
            .GetSection(DocumentIntelligenceOptions.SectionName)
            .Get<DocumentIntelligenceOptions>() ?? new DocumentIntelligenceOptions();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        var provider = configuration["Database:Provider"] ?? "Sqlite";

        services.AddDbContext<ReceiptDbContext>(options =>
        {
            if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                // EnableRetryOnFailure helps with transient Azure SQL / network blips.
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(15),
                        errorNumbersToAdd: null);
                    sql.CommandTimeout(60);
                });
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });

        services.AddScoped<IReceiptRepository, ReceiptRepository>();
        services.AddScoped<IProcessingBatchRepository, ProcessingBatchRepository>();
        services.AddSingleton<IReceiptFieldExtractor, ReceiptFieldExtractor>();
        services.AddSingleton<AmazonCsvReceiptImporter>();
        services.AddHttpContextAccessor();
        services.AddScoped<IOpenAiUsageRecorder, OpenAiUsageRecorder>();
        services.AddScoped<IAiCorrectionLearningService, OpenAiCorrectionLearningService>();
        services.AddHttpClient<IAiFieldEnrichmentService, OpenAiFieldEnrichmentService>();
        services.AddScoped<IDocumentReceiptAnalyzer, AzureDocumentReceiptAnalyzer>();
        services.AddSingleton<IReceiptBlobStore, AzureReceiptBlobStore>();

        // When Azure Document Intelligence is configured it replaces local Tesseract OCR.
        if (!documentIntelligence.IsConfigured)
        {
            services.AddScoped<ITextExtractor, ImageOcrTextExtractor>();
            services.AddScoped<ITextExtractor, PdfTextExtractor>();
        }

        services.AddScoped<IReceiptProcessingService, ReceiptProcessingService>();
        services.AddSingleton<IExcelExportService, ExcelExportService>();
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddScoped<IUserAuthService, UserAuthService>();
        services.AddSingleton<ILoginVerificationService, LoginVerificationService>();
        services.AddSingleton<IEmailChangeVerificationService, EmailChangeVerificationService>();
        services.AddSingleton<IPasswordResetService, PasswordResetService>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
