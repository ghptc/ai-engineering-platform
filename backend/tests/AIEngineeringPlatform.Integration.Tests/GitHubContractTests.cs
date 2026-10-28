using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed class GitHubContractTests
{
    [Fact]
    public async Task AppScopesTokensAndDeduplicatesPullRequestAndReview()
    {
        using var rsa = RSA.Create(2048);
        var keyPath = Path.Combine(Path.GetTempPath(), "aep-test-" + Guid.NewGuid().ToString("N") + ".pem");
        await File.WriteAllTextAsync(keyPath, rsa.ExportPkcs8PrivateKeyPem());
        try
        {
            var handler = new GitHubHandler();
            var source = new GitHubSourceControl(new HttpClient(handler), "123", keyPath, "test-secret");
            var repository = new RepositoryConnection("test-owner", "test-repo", 123);
            var arguments = JsonSerializer.SerializeToElement(new { branch = "engineering/test-run", title = "Test PR", body = "Test body" });
            var first = await source.ExecuteAsync(repository, "CreatePullRequest", arguments, "create-op", default);
            var second = await source.ExecuteAsync(repository, "CreatePullRequest", arguments, "create-op", default);
            Assert.Equal(first, second); Assert.Equal(1, handler.PullRequestCreates);
            var review = JsonSerializer.SerializeToElement(new { number = 7, decision = "APPROVE", body = "Verified contract fixture" });
            var published = await source.ExecuteAsync(repository, "SubmitCodeReview", review, "review-op", default);
            var duplicate = await source.ExecuteAsync(repository, "SubmitCodeReview", review, "review-op", default);
            Assert.Equal(published, duplicate); Assert.Equal(1, handler.ReviewCreates);
            Assert.Contains("\"repositories\":[\"test-repo\"]", handler.TokenScope);
            Assert.Contains("\"contents\":\"write\"", handler.TokenScope);
            Assert.Equal("installation-token", handler.LastRepositoryToken);
        }
        finally { File.Delete(keyPath); }
    }

    [Fact]
    public void SecretOutputIsRedacted()
    {
        var output = SecretRedactor.Redact("Bearer header: Authorization: Bearer token-value\nMongoDB mongodb://owner:password@example/db\nsk-proj-12345678901234567890");
        Assert.DoesNotContain("token-value", output); Assert.DoesNotContain("owner:password", output); Assert.DoesNotContain("sk-proj-", output);
    }

    private sealed class GitHubHandler : HttpMessageHandler
    {
        public int PullRequestCreates; public int ReviewCreates;
        public string TokenScope = ""; public string LastRepositoryToken = "";
        private string? pullRequest; private string? publishedReview;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            object response;
            if (path.EndsWith("/access_tokens"))
            {
                TokenScope = body;
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.Equal(3, request.Headers.Authorization.Parameter!.Split('.').Length);
                response = new { token = "installation-token", expires_at = DateTimeOffset.UtcNow.AddHours(1) };
            }
            else
            {
                LastRepositoryToken = request.Headers.Authorization!.Parameter!;
                if (path.EndsWith("/reviews"))
                {
                    if (request.Method == HttpMethod.Get) return Json(publishedReview == null ? "[]" : "[" + publishedReview + "]");
                    ReviewCreates++;
                    using var input = JsonDocument.Parse(body);
                    publishedReview = JsonSerializer.Serialize(new { id = 99, state = "APPROVED", body = input.RootElement.GetProperty("body").GetString() });
                    return Json(publishedReview);
                }
                if (path.EndsWith("/pulls/7")) response = new { number = 7, changed_files = 1, head = new { sha = "test-sha" } };
                else if (path.EndsWith("/pulls"))
                {
                    if (request.Method == HttpMethod.Get) return Json(pullRequest == null ? "[]" : "[" + pullRequest + "]");
                    PullRequestCreates++; pullRequest = "{\"id\":17,\"number\":7,\"html_url\":\"https://github.com/test-owner/test-repo/pull/7\"}"; return Json(pullRequest);
                }
                else response = new { full_name = "test-owner/test-repo", default_branch = "main" };
            }
            return Json(JsonSerializer.Serialize(response));
        }
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }
}
