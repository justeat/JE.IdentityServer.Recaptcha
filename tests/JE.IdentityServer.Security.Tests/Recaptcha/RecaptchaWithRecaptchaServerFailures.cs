﻿using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using JE.IdentityServer.Security.Recaptcha.Services;
using JE.IdentityServer.Security.Tests.Infrastructure;
using JustFakeIt;
using Newtonsoft.Json;
using NUnit.Framework;

namespace JE.IdentityServer.Security.Tests.Recaptcha
{
    public class RecaptchaWithRecaptchaServerFailures
    {
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.InternalServerError)]
        public async Task RecaptchaWithInvalidRecaptchaAnswer_WithAnswerAsHeaderValue_ShouldChallenge(HttpStatusCode returnedStatusCode)
        {
            using (var fakeRecaptchaServer = new FakeServer())
            {
                fakeRecaptchaServer.Start();
                fakeRecaptchaServer.Expect.Get("/?secret=private_key&response=correct_response")
                    .Returns(returnedStatusCode, string.Empty);

                var identityServerBuilder = new IdentityServerWithRecaptcha()
                    .WithProtectedGrantType("password")
                    .WithPrivateKey("private_key")
                    .WithVerificationUri(fakeRecaptchaServer.BaseUri)
                    .WithNumberOfAllowedLoginFailuresPerIpAddress(1);

                using (var server = identityServerBuilder.Build())
                {
                    var response = await server.CreateNativeLoginRequest()
                        .WithUsername("jeuser")
                        .WithPassword("Passw0rd")
                        .WithHttpHeaderRecaptchaResponseBase64("correct_response")
                        .Build()
                        .PostAsync();
                    response.StatusCode.Should().Be(HttpStatusCode.OK);

                    identityServerBuilder.RecaptchaMonitor.HasCompletedChallenge.Should().BeTrue();
                    identityServerBuilder.RecaptchaMonitor.ChallengeCompletedInvokeCount.Should().Be(2);
                    identityServerBuilder.RecaptchaMonitor.ResponseContext.State.Should()
                        .Be(RecaptchaState.ChallengeSucceeded);
                }
            }
        }

        [Test]
        public async Task RecaptchaWithInvalidRecaptchaAnswer_WithAnswerAsAcrValue_ShouldChallenge()
        {
            using (var fakeRecaptchaServer = new FakeServer())
            {
                fakeRecaptchaServer.Start();
                fakeRecaptchaServer.Expect.Get("/?secret=private_key&response=correct_response")
                    .Returns(HttpStatusCode.OK, JsonConvert.SerializeObject(new RecaptchaVerificationResponse
                    {
                        Succeeded = false
                    }));

                var identityServerBuilder = new IdentityServerWithRecaptcha()
                    .WithProtectedGrantType("password")
                    .WithPrivateKey("private_key")
                    .WithVerificationUri(fakeRecaptchaServer.BaseUri)
                    .WithNumberOfAllowedLoginFailuresPerIpAddress(1)
                    .WithFailuresForIpAddress("192.168.1.101", 1);

                using (var server = identityServerBuilder.Build())
                {
                    var response = await server.CreateNativeLoginRequest()
                        .WithUsername("jeuser")
                        .WithPassword("Passw0rd")
                        .WithRecaptchaResponseAsAcrValue("correct_response")
                        .Build()
                        .PostAsync();
                    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                    response.Headers.WwwAuthenticate.Should().Contain(h => h.Scheme == "recaptcha");
                    response.Headers.WwwAuthenticate.Should().Contain(h => h.Parameter == @"url=""/recaptcha/platform""");

                    identityServerBuilder.RecaptchaMonitor.HasIssuedChallenge.Should().BeTrue();
                }
            }
        }
    }
}
