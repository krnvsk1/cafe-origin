namespace Cafe.API.Models
{
    public static class AppRoles
    {
        public const string User = "user";
        public const string Admin = "admin";
        public const string Manager = "manager";
        public const string Courier = "courier";
        /// <summary>Техническая поддержка: всегда все права, права роли не меняются; назначает и снимает только поддержка</summary>
        public const string Support = "support";

        public static readonly string[] AllRoles = { User, Admin, Manager, Courier, Support };
    }
}
