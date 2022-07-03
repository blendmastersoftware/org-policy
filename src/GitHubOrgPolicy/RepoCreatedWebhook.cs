using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Octokit;
using Octokit.Webhooks;
using Octokit.Webhooks.Events;
using Octokit.Webhooks.Events.Repository;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace GitHubOrgPolicy
{
    public static class RepoCreatedWebhook
    {
        [FunctionName("RepoCreatedWebhook")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest request,
            ILogger log)
        {
            // parse the X-GitHub-Event request header to identify the event type
            var webhookHeaders = WebhookHeaders.Parse(request.Headers);

            log.LogInformation($"Webhook {webhookHeaders.Event} event type received");

            // only process repository events
            if (webhookHeaders.Event == WebhookEventType.Repository)
            {
                // deserialize the webhook payload object
                var repoEvent = await JsonSerializer.DeserializeAsync<RepositoryEvent>(request.Body);

                log.LogInformation($"Repository {repoEvent.Action} event received");

                // only process repository created events
                if (repoEvent.Action == RepositoryActionValue.Created)
                {
                    log.LogInformation($"Repo {repoEvent.Action} event received for {repoEvent.Repository.FullName} repo");

                    // retrieve GitHub access token
                    var authToken = Environment.GetEnvironmentVariable("GitHubAuthToken", EnvironmentVariableTarget.Process);

                    // initialize the GitHub API client
                    var client = new GitHubClient(new ProductHeaderValue(nameof(RepoCreatedWebhook)));
                    client.Credentials = new Credentials(authToken);

                    // create a new issue
                    var issue = await client.Issue.Create(repoEvent.Repository.Id, new NewIssue("Enable branch protection"));

                    // configure branch protection settings
                    var branchProtectionSettings = new BranchProtectionSettingsUpdate(
                        new BranchProtectionRequiredReviewsUpdate(false, false, 1)
                    );

                    // update branch protection settings
                    await client.Repository.Branch.UpdateBranchProtection(repoEvent.Repository.Id, repoEvent.Repository.DefaultBranch, branchProtectionSettings);

                    // retrieve the GitHub user to mention in the comment
                    var user = Environment.GetEnvironmentVariable("GitHubUser", EnvironmentVariableTarget.Process);

                    // add a comment to the issue and @mention the user above
                    await client.Issue.Comment.Create(repoEvent.Repository.Id, issue.Number, $"@{user} branch protection enabled successfully:\n" +
                        "* Require a pull request before merging\n" +
                        "  * Require approvals (1)");

                    // close the issue on completion
                    await client.Issue.Update(repoEvent.Repository.Id, issue.Number, new IssueUpdate
                    {
                        State = ItemState.Closed
                    });

                    log.LogInformation($"Branch protection enabled for default branch {repoEvent.Repository.DefaultBranch}");
                }
            }

            return new NoContentResult();
        }
    }
}
