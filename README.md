# AI Code Reviewer

> A study project that automatically reviews GitHub Pull Requests using a local LLM, built with .NET 10, RabbitMQ, and Clean Architecture.

---

## What is this project?

This project was built to **learn and practice AI-integrated backend development** in .NET. The idea is simple: whenever a Pull Request is opened or updated on GitHub, the system automatically fetches the changed code, sends it to a local LLM (Ollama + llama3.2:3b) for analysis, and posts review comments directly on the PR — just like a human reviewer would.

Beyond the AI aspect, this project is also a hands-on study of several important backend patterns:

- **Webhooks** — how to securely receive real-time events from external services
- **Message Queues** — how to decouple slow operations (LLM inference) from fast HTTP responses
- **Clean Architecture** — how to organize a .NET solution so each layer has a single responsibility
- **CQRS with MediatR** — how to structure use cases as commands and handlers
- **Structured LLM output** — how to prompt an LLM to return JSON and parse it reliably

---

## How it works

```
GitHub PR opened/updated
        │
        ▼
POST /api/webhooks/github         ← Api receives the event
        │
        ├─ 1. Validate HMAC-SHA256 signature (security)
        ├─ 2. Filter event type and PR action
        ├─ 3. Parse payload (owner, repo, PR number, commit SHA)
        ├─ 4. Publish ReviewRequestedMessage to RabbitMQ
        └─ 5. Return 200 OK immediately  ← GitHub gets a fast response
                │
                ▼ (async — happens in the background)
        ReviewRequestConsumer (Worker)
                │
                ├─ 6. Fetch PR diff via GitHub API (Octokit.NET)
                ├─ 7. Send diff to llama3.2:3b (Ollama) for review
                ├─ 8. Parse the LLM's JSON response into ReviewComment objects
                └─ 9. Post review comments back to the PR on GitHub
```

The key insight is the **decoupling between steps 5 and 6**: the API responds to GitHub immediately, while the Worker processes the review at its own pace. This is necessary because LLM inference on CPU can take several minutes — GitHub would time out if we blocked the HTTP response waiting for it.

---

## Key Concepts Learned

### 1. Webhooks & HMAC-SHA256 Security

A **webhook** is an HTTP callback — instead of your app polling an external service for updates, the service sends a POST request to your app when something happens.

Security is critical: anyone on the internet could send fake requests to your webhook endpoint. GitHub solves this by signing every payload with **HMAC-SHA256**:

```
signature = HMAC-SHA256(secret, requestBody)
```

On our side, we compute the same signature and compare. If they match, the request genuinely came from GitHub.

One important detail: we use `CryptographicOperations.FixedTimeEquals()` instead of `==` for the comparison. Regular string comparison **short-circuits** on the first mismatch, creating a timing side-channel attack. `FixedTimeEquals` always takes the same time regardless of where the strings differ.

```csharp
// ❌ Vulnerable to timing attacks
return actualHex == expectedHex;

// ✅ Safe — constant-time comparison
return CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(actualHex),
    Encoding.UTF8.GetBytes(expectedHex));
```

### 2. Message Queues (RabbitMQ + MassTransit)

A **message queue** is a buffer between a producer (the API) and a consumer (the Worker). The producer publishes a message and moves on — it doesn't wait for the consumer to finish.

Key benefits in this project:
- **Decoupling**: the API doesn't know anything about the LLM or GitHub API
- **Resilience**: if the Worker crashes, messages stay in the queue and are retried automatically
- **Scalability**: run multiple Worker instances in parallel to process more reviews concurrently

**MassTransit** is the abstraction layer on top of RabbitMQ. It handles serialization, queue creation, retry policies, and dead-letter queues automatically.

```
Producer (Api)          RabbitMQ          Consumer (Worker)
     │                     │                    │
     │  Publish(message)   │                    │
     │────────────────────▶│                    │
     │   200 OK            │   Consume(message) │
     │◀────────────────────│───────────────────▶│
     │                     │                    │ process...
     │                     │              ACK   │
     │                     │◀───────────────────│
```

**ACK / NACK**: RabbitMQ requires consumers to acknowledge (ACK) each message after processing. If the Worker throws an exception, MassTransit sends a NACK — the message goes back to the queue and will be retried.

### 3. Clean Architecture

The solution is split into four layers, each with a strict dependency rule: **inner layers never depend on outer layers**.

```
┌─────────────────────────────────────────┐
│                  Api / Worker           │  ← Entry points (HTTP, Queue)
├─────────────────────────────────────────┤
│              Infrastructure             │  ← External I/O (GitHub, Ollama, RabbitMQ)
├─────────────────────────────────────────┤
│               Application              │  ← Use cases, interfaces, commands
├─────────────────────────────────────────┤
│                  Domain                 │  ← Business entities (no dependencies)
└─────────────────────────────────────────┘
         Dependencies point inward only →
```

The Application layer defines **interfaces** (ports) like `IGitHubService` and `ILlmReviewService`. Infrastructure implements them. This means:
- You can swap Ollama for Claude API by changing only Infrastructure
- You can unit test handlers by injecting mock implementations
- The business logic never imports Octokit, MassTransit, or Semantic Kernel

### 4. CQRS with MediatR

**CQRS** (Command Query Responsibility Segregation) separates write operations (Commands) from read operations (Queries). A **Command** expresses an intent to change the system.

**MediatR** implements the Mediator pattern — instead of objects calling each other directly, they send messages through a central mediator:

```
WebhooksController                 MediatR                  HandleWebhookHandler
        │                             │                              │
        │  Send(HandleWebhookCommand) │                              │
        │────────────────────────────▶│  Handle(command)             │
        │                             │─────────────────────────────▶│
        │        HandleWebhookResult  │              HandleWebhookResult
        │◀────────────────────────────│◀─────────────────────────────│
```

The controller doesn't know `HandleWebhookHandler` exists. MediatR finds the right handler automatically by matching the command type.

MediatR also supports **Pipeline Behaviors** — middleware that wraps every command, perfect for cross-cutting concerns like logging, validation, or transactions.

### 5. Structured LLM Output (Prompt Engineering)

LLMs generate text — they don't natively return structured data. To get a reliable JSON response, we use **structured output prompting**:

1. Tell the model exactly what format to return and show an example
2. Explicitly forbid extra text or markdown wrappers
3. Parse the response defensively (strip code fences, handle parsing errors)

```csharp
// System prompt excerpt
"Return ONLY a valid JSON array, no extra text, no markdown fences"
"Format: [{\"filePath\": \"src/Foo.cs\", \"line\": 42, \"severity\": \"warning\", \"body\": \"...\"}]"

// Defensive parser: handle cases where the LLM wraps JSON in ```
if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
if (json.EndsWith("```")) json = json[..json.LastIndexOf("```")];
```

### 6. Options Pattern (IOptions\<T\>)

Instead of injecting `IConfiguration` directly into handlers (which couples them to the configuration system), we define typed settings classes:

```csharp
// Define the shape of the config section
public sealed class GitHubSettings
{
    public string Token { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
}

// Register in DI (reads from appsettings.json / environment variables)
services.Configure<GitHubSettings>(configuration.GetSection("GitHub"));

// Inject in handlers — strongly typed, no magic strings
public HandleWebhookHandler(IOptions<GitHubSettings> settings, ...)
{
    _settings = settings.Value;
}
```

---

## Architecture

### Projects

| Project | Type | Responsibility |
|---|---|---|
| `Domain` | Class Library | Business entities — `ReviewRequest`, `ReviewResult`, `ReviewComment` |
| `Application` | Class Library | Interfaces, Commands, Handlers (MediatR), Settings |
| `Infrastructure` | Class Library | GitHub (Octokit.NET), LLM (Semantic Kernel + Ollama), Messaging (MassTransit) |
| `Api` | ASP.NET Core Web API | Receives GitHub webhooks, publishes to RabbitMQ |
| `Worker` | .NET Worker Service | Consumes RabbitMQ messages, runs the review pipeline |

### Review Pipeline

```
HandleWebhookHandler          ProcessReviewHandler
(Application)                 (Application)
      │                              │
      │ IReviewPublisher             │ IGitHubService
      ▼                              ▼
RabbitMqPublisher            GitHubService
(Infrastructure)              (Infrastructure / Octokit.NET)
      │                              │
      ▼                              │ ILlmReviewService
   RabbitMQ ──────────────▶          ▼
ReviewRequestConsumer        OllamaReviewService
(Infrastructure)              (Infrastructure / Semantic Kernel)
      │                              │
      └──── MediatR.Send ────────────┘
```

### Tech Stack

| Component | Technology |
|---|---|
| API | .NET 10 ASP.NET Core Web API |
| Worker | .NET 10 Worker Service |
| Message Broker | RabbitMQ 3 |
| Messaging Abstraction | MassTransit 8 |
| Use Case Orchestration | MediatR 14 |
| LLM | Ollama + llama3.2:3b (local) |
| AI Orchestration | Semantic Kernel |
| GitHub Integration | Octokit.NET |
| Containers | Docker + Docker Compose |

---

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (v24+)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10) — for local development only
- [ngrok](https://ngrok.com/) — to expose your local API to GitHub's webhook system
- A GitHub account with a repository to test against
- ~2.5 GB free disk space for the llama3.2:3b model

---

## Running Locally

### 1. Clone the repository

```bash
git clone https://github.com/your-username/ai-code-reviewer
cd ai-code-reviewer
```

### 2. Configure environment variables

```bash
cp .env.example .env
```

Edit `.env` and fill in your values:

```env
GITHUB__TOKEN=ghp_your_personal_access_token_here
GITHUB__WEBHOOKSECRET=any_random_string_you_choose
```

**Getting a GitHub Personal Access Token:**
1. Go to GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. Generate a new token with `repo` scope (needed to read PR files and post reviews)

### 3. Start all services

```bash
docker-compose up -d
```

This starts: `api` (port 5000), `worker`, `rabbitmq` (ports 5672 + 15672), `ollama` (port 11434), and `ollama-init` (pulls the model).

```bash
# Wait for the llama3.2:3b model to download (~2 GB, first run only)
docker logs -f ai-code-reviewer-ollama-init-1
```

### 4. Expose your local API with ngrok

GitHub needs a public URL to send webhooks to. ngrok creates a secure tunnel to your localhost:

```bash
ngrok http 5000
```

Copy the HTTPS URL shown (e.g., `https://abc123.ngrok-free.app`).

### 5. Configure the GitHub webhook

1. Go to your test repository → Settings → Webhooks → Add webhook
2. **Payload URL**: `https://abc123.ngrok-free.app/api/webhooks/github`
3. **Content type**: `application/json`
4. **Secret**: the same value you set in `GITHUB__WEBHOOKSECRET`
5. **Events**: select "Pull requests"
6. Click "Add webhook"

### 6. Test it

Open a Pull Request in your repository. You should see:
- The API receive the webhook and return 200 OK (visible in the GitHub webhook delivery log)
- The RabbitMQ Management UI (`http://localhost:15672`, guest/guest) show the message being consumed
- After a few minutes (LLM processing time), review comments appear on the PR

---

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/webhooks/github` | Receives GitHub webhook events for PR reviews |

---

## Notes

- **LLM speed**: llama3.2:3b runs on CPU by default — expect 2–5 minutes per review. For GPU acceleration, add the NVIDIA runtime to the `ollama` service in `docker-compose.yml`.
- **Model quality**: llama3.2:3b is a compact model chosen for accessibility. For better review quality, swap it for `llama3.1:8b` or `codellama:13b` (requires more RAM).
- **RabbitMQ Management UI**: available at `http://localhost:15672` (default credentials: guest/guest). Useful for inspecting queues, messages, and consumer activity.
- **ngrok free tier**: the public URL changes every time you restart ngrok. Update the webhook URL in GitHub settings after each restart.
