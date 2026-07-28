# HST Receipts Configuration Guide

## Environment-Based Configuration

The application uses ASP.NET Core's environment-based configuration. Configuration files are loaded based on the `ASPNETCORE_ENVIRONMENT` variable.

### Configuration Files

- **appsettings.json** - Default/Development configuration (LocalDB)
- **appsettings.Development.json** - Development logging overrides
- **appsettings.Development.local.json** - Development secrets (git-ignored)
- **appsettings.Production.json** - Production configuration (Azure SQL)
- **appsettings.Production.local.json** - Production secrets (git-ignored)

### Environment Setup

#### Development
```bash
set ASPNETCORE_ENVIRONMENT=Development
# or
$env:ASPNETCORE_ENVIRONMENT = "Development"
```

Uses:
- **Database**: LocalDB (local machine development only)
- **Azure Services**: Disabled by default, available for configuration

#### Production
```bash
set ASPNETCORE_ENVIRONMENT=Production
# Azure App Service sets this automatically
```

Uses:
- **Database**: Azure SQL Server
- **Azure Services**: Role-based access (see below)

## Database Configuration

**CRITICAL: Connection string is environment-specific. Owner role will manage users in the connected database.**

### Development (LocalDB)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=HstReceipts;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
  },
  "Database": {
    "Provider": "SqlServer"
  }
}
```
- Set `ASPNETCORE_ENVIRONMENT=Development` or leave unset (defaults to Development)
- Connects to: **Local development database**
- Owner can modify email/manage: **Development users only**
- Use for: Testing, local development, QA

### Production (Azure SQL)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=tcp:YOUR_SERVER.database.windows.net,1433;Initial Catalog=YOUR_DB;User ID=YOUR_USER;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;..."
  },
  "Database": {
    "Provider": "SqlServer"
  }
}
```
- Set `ASPNETCORE_ENVIRONMENT=Production` (required on Azure App Service)
- Connects to: **Azure SQL Server**
- Owner can modify email/manage: **Production users only**
- Use for: Live production environment

### How It Works
1. Application reads `ASPNETCORE_ENVIRONMENT` variable
2. Loads corresponding `appsettings.{Environment}.json`
3. Uses connection string from that environment's config
4. Owner role connects to and manages users in that specific database
5. Retry logic automatically applied for Azure SQL transient failures

### Safety Checklist
- ✓ Development machine: Ensure `ASPNETCORE_ENVIRONMENT=Development` (or unset)
- ✓ Production deployment: Verify `ASPNETCORE_ENVIRONMENT=Production` is set
- ✓ Azure App Service: Environment variable is set in Application Settings
- ✓ Owner role: Can only modify users in the connected database (environment-specific)

## Role-Based Azure Services

**Only Owner role can use Azure services** for receipt extraction, regardless of environment (Development or Production). This ensures cost control and feature tiering. Owner can configure and use Azure services in both environments if credentials are provided.

### Azure Document Intelligence (AI Receipt OCR)

**When Available**:
- Owner role with valid Azure Document Intelligence configuration
- Provides 95%+ accuracy for structured receipt extraction

**When Not Available**:
- All roles fall back to local Tesseract OCR
- Provides ~70-80% accuracy for structured extraction

**Configuration**:
```json
{
  "DocumentIntelligence": {
    "Owner": {
      "Enabled": true,
      "Endpoint": "https://YOUR_RESOURCE.cognitiveservices.azure.com/",
      "ApiKey": "YOUR_API_KEY",
      "ModelId": "prebuilt-receipt",
      "Locale": "en-CA",
      "FillGapsWithRules": true
    },
    "Admin": {
      "Enabled": false,
      "Endpoint": "",
      "ApiKey": ""
    }
  }
}
```

### Azure Blob Storage (Receipt Storage)

**When Available**:
- Owner role only (when configured and enabled)
- Stores original receipt images for archival and re-processing

**When Not Available**:
- Receipts are processed but not persisted to blob storage
- OCR results are still saved to database

**Configuration**:
```json
{
  "BlobStorage": {
    "Enabled": true,
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=YOUR_ACCOUNT;AccountKey=YOUR_KEY;...",
    "InboxContainer": "receipts-inbox",
    "ResultsContainer": "receipts-results"
  }
}
```

## Role Authorization Matrix

| Role | Database | Local OCR | Azure AI Document Intelligence | Blob Storage |
|------|----------|-----------|------|--------------|
| **Owner** | ✓ | ✓ (fallback) | ✓ if configured* | ✓ if configured* |
| Admin | ✓ | ✓ (fallback) | ✗ | ✗ |
| Officer | ✓ | ✓ (fallback) | ✗ | ✗ |
| Demo | ✓ | ✓ (fallback) | ✗ | ✗ |

*Owner access to Azure services is **configuration-driven**: if API key + endpoint are set in environment variables/Key Vault, Owner can use Azure services regardless of environment.

## Secret Management

**CRITICAL: No secrets should ever be committed to git.**

### Configuration File Structure
- `appsettings.json` - Default development config (LocalDB, no secrets)
- `appsettings.Production.json` - Production template with placeholders (no secrets)
- `appsettings.Development.local.json` - Development secrets (git-ignored)
- `appsettings.Production.local.json` - Production secrets (git-ignored)

The `.gitignore` file protects:
```
appsettings.*.local.json          # All local secret files ignored
!appsettings.*.local.json.example # But keep example templates
```

## Setup Instructions

### Development Setup

1. Copy `appsettings.Development.local.json.example` to `appsettings.Development.local.json`
2. **Edit ONLY the `.local.json` file** with your development secrets:
   - Azure SQL connection string (or keep LocalDB)
   - Azure Document Intelligence credentials (Owner can test Azure services)
   - Azure Blob Storage connection string (optional)
   - SMTP credentials for email verification
3. Run migrations: `dotnet ef database update`
4. Start: `dotnet run`

### Production Setup

**Option 1: Environment Variables (Recommended)**
1. Set environment variables on Azure App Service:
   - `ConnectionStrings__DefaultConnection` = Azure SQL connection string
   - `DocumentIntelligence__Owner__Endpoint` = Owner AI endpoint
   - `DocumentIntelligence__Owner__ApiKey` = Owner AI API key
   - `BlobStorage__ConnectionString` = Blob storage connection string
   - `Smtp__Username` = Email username
   - `Smtp__Password` = Email password
2. Set `ASPNETCORE_ENVIRONMENT=Production`
3. Deploy application
4. Run migrations: `dotnet ef database update`

**Option 2: Azure Key Vault (Enterprise)**
1. Create Azure Key Vault secrets
2. Configure Managed Identity on App Service
3. App Service reads secrets from Key Vault automatically
4. `appsettings.Production.json` contains only placeholders

**Option 3: Local .local.json File (Development Only)**
1. Create `appsettings.Production.local.json` from template
2. Add secrets to `.local.json` file
3. Deploy with `.local.json` file (NOT recommended for production)

## Security Best Practices

- ✅ **Development**: Use `appsettings.Development.local.json` (git-ignored)
- ✅ **Production**: Use environment variables or Azure Key Vault
- ✅ **Never commit** `*.local.json` files to git
- ✅ **Never hardcode** secrets in `appsettings.json` or `appsettings.Production.json`
- ✅ **Rotate API keys** regularly in production
- ✅ **Use Managed Identity** on Azure App Service (no connection string needed)
- ✅ **Enable SSL** for all Azure connections
- ✅ **Use app passwords** instead of account passwords for SMTP

## Monitoring

### Log Levels
- Development: Information (verbose)
- Production: Information (reduced ASP.NET Core logs to Warning)

### Azure Document Intelligence Usage
- Check usage in Azure Portal → Cognitive Services → Usage & Quota
- Monitor costs for Owner-initiated OCR operations
