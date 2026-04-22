# Compliance Automator API (AutoAnalyst)

Welcome to the **Compliance Automator** (formerly RPA Automation) repository. This is the .NET 8 microservice responsible for orchestrating background browser automation tasks via Headless Playwright.

## 🚀 Overview

This API serves as the core automation engine for the Koncilia ecosystem. It receives HTTP requests to start complex, multi-step web scraping and RPA processes, delegating the heavy lifting to Playwright in the background.

### Key Features
* **Ultra-Detached Worker Pattern**: When a process is triggered via `POST /api/process/start`, the API instantly returns a `JobId`. The actual browser automation runs on a detached background thread using `IServiceScopeFactory`, ensuring the HTTP response is never blocked and the service never crashes from disposed contexts.
* **Headless Playwright Automation**: Safely executes multi-stage navigation, form-filling, and downloads invisibly within a Linux Docker container.
* **Reactive Status Polling**: The API provides a `GET /api/process/status/{jobId}` endpoint that the Angular frontend pings to provide real-time updates to the user.
* **Windows Authentication Support**: Configured to pass NTLM credentials securely to internal corporate systems during the automation cycle.

## 🛠️ Tech Stack
* **Framework**: ASP.NET Core 8.0 Minimal APIs
* **Automation Engine**: Microsoft Playwright (`mcr.microsoft.com/playwright/dotnet:v1.59.0-jammy`)
* **Containerization**: Docker

## 🔐 Security Notice
The `appsettings.json` file contains sensitive configuration details, including Windows Authentication credentials. This file is explicitly excluded via `.gitignore` to prevent unauthorized access. Ensure you have the proper local `appsettings.json` configured before running the service.

## 🐳 Running Locally

To build and run this microservice using Docker:

```powershell
docker build -t compliance-automator-api .
docker run -p 8080:8080 compliance-automator-api
```

Once running, the API will be available at `http://localhost:8080`.

## 📁 Directory Structure
* `Controllers/`: Contains the `ProcessController` handling HTTP requests.
* `Services/`: Core business logic, including `PlaywrightService` and `JobManager`.
* `Models/`: Data Transfer Objects (DTOs) and Enums (e.g., `JobStatus`).
* `Logs/`: Output directory for execution logs (created dynamically).
