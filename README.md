# \# MyApi

# 

# \[!\[CI/CD](https://github.com/birupakhya2000/MyApi/actions/workflows/ci.yml/badge.svg)](https://github.com/birupakhya2000/MyApi/actions/workflows/dotnet.yml)

# 

# A .NET 10 Minimal API that returns the current time in any time zone, with user accounts and saved favorite time zones.

# 

# \## Features

# \- Time and time zone endpoints, documented with OpenAPI and Scalar

# \- JWT authentication, with passwords hashed using ASP.NET's PasswordHasher

# \- EF Core with Azure SQL (SQLite locally and in tests)

# \- Rate limiting, CORS and security headers

# \- Application Insights monitoring (OpenTelemetry)

# 

# \## CI/CD (GitHub Actions)

# Format check, vulnerability scan, build, test with coverage, then deploy to Staging, smoke test, and promote the same artifact to Production on Azure App Service. Azure login uses OIDC, so there are no stored passwords.

# 

# \## Run locally

# &#x20;   dotnet run --project src\\MyApi.Api

# Then open /scalar.

# 

# \## Run tests

# &#x20;   dotnet test

