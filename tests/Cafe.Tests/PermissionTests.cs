using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Cafe.API.Authorization;
using Xunit;

namespace Cafe.Tests
{
    public class PermissionTests
    {
        private static ClaimsPrincipal Principal(string[] roles, params string[] permissions)
        {
            var claims = roles.Select(r => new Claim(ClaimTypes.Role, r))
                .Concat(permissions.Select(p => new Claim(Permissions.ClaimType, p)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        [Fact]
        public void Catalog_HasUniqueKeys_AndDefaultsUseKnownKeys()
        {
            Assert.Equal(Permissions.All.Count, Permissions.Keys.Count);
            foreach (var defaults in Permissions.Defaults.Values)
                Assert.All(defaults, p => Assert.Contains(p, Permissions.Keys));
        }

        [Fact]
        public void Support_HasEveryPermission()
        {
            var support = Principal(new[] { "support" });
            Assert.True(support.IsSupport());
            Assert.All(Permissions.Keys, p => Assert.True(support.HasPermission(p)));
        }

        [Fact]
        public void Admin_HasOnlyItsConfiguredPermissions()
        {
            var admin = Principal(new[] { "admin" }, Permissions.Menu);
            Assert.False(admin.IsSupport());
            Assert.True(admin.HasPermission(Permissions.Menu));
            Assert.False(admin.HasPermission(Permissions.Roles));
        }

        [Fact]
        public void User_HasOnlyGrantedPermissions()
        {
            var cook = Principal(new[] { "povar" }, Permissions.Kitchen);
            Assert.True(cook.HasPermission(Permissions.Kitchen));
            Assert.False(cook.HasPermission(Permissions.Menu));
            Assert.True(cook.HasAnyPermission(Permissions.Menu, Permissions.Kitchen));
        }

        [Fact]
        public async Task PolicyProvider_BuildsAnyOfPolicy()
        {
            var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));
            var policy = await provider.GetPolicyAsync(new HasPermissionAttribute(Permissions.Kitchen, Permissions.Delivery).Policy!);

            Assert.NotNull(policy);
            var requirement = Assert.Single(policy!.Requirements.OfType<PermissionRequirement>());
            Assert.Equal(new[] { Permissions.Kitchen, Permissions.Delivery }, requirement.Any);
        }

        [Fact]
        public async Task Handler_SucceedsWhenAnyPermissionMatches()
        {
            var requirement = new PermissionRequirement(new[] { Permissions.Kitchen, Permissions.Delivery });
            var courier = Principal(new[] { "courier" }, Permissions.Delivery);
            var context = new AuthorizationHandlerContext(new[] { requirement }, courier, null);

            await new PermissionHandler().HandleAsync(context);
            Assert.True(context.HasSucceeded);

            var client = Principal(new[] { "user" });
            var denied = new AuthorizationHandlerContext(new[] { requirement }, client, null);
            await new PermissionHandler().HandleAsync(denied);
            Assert.False(denied.HasSucceeded);
        }
    }
}
