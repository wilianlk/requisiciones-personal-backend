using System.Collections.Generic;
using System.Linq;

namespace BackendRequisicionPersonal.Helpers
{
    public static class EmailHelper
    {
        /// <summary>
        /// Normaliza un correo electrónico a minúsculas y sin espacios
        /// </summary>
        public static string NormalizeEmail(string? email)
            => (email ?? "").Trim().ToLowerInvariant();

        /// <summary>
        /// Obtiene una lista de correos únicos y normalizados
        /// </summary>
        public static List<string> DistinctNormalizedEmails(IEnumerable<string> emails)
            => emails
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(NormalizeEmail)
                .Distinct()
                .ToList();

        /// <summary>
        /// Obtiene el primer correo válido de una lista
        /// </summary>
        public static string? FirstEmail(IEnumerable<string> correosRaw)
            => correosRaw?
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?
                .Trim()
                .ToLowerInvariant();
    }
}
