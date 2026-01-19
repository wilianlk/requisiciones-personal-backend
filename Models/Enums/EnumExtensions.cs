using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace BackendRequisicionPersonal.Models.Enums
{
    public static class EnumExtensions
    {
        public static string GetDescription(this Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            if (field == null) return value.ToString();

            var attribute = field.GetCustomAttribute<DescriptionAttribute>();
            return attribute?.Description ?? value.ToString();
        }

        public static EstadoRequisicion? ToEstadoRequisicion(this string? estado)
        {
            if (string.IsNullOrWhiteSpace(estado))
                return null;

            var normalized = estado.Trim().ToUpperInvariant();

            // Buscar por descripción
            foreach (EstadoRequisicion value in Enum.GetValues(typeof(EstadoRequisicion)))
            {
                if (value.GetDescription().Equals(normalized, StringComparison.OrdinalIgnoreCase))
                    return value;
            }

            // Buscar por nombre del enum
            if (Enum.TryParse<EstadoRequisicion>(normalized.Replace(" ", ""), true, out var result))
                return result;

            return null;
        }

        public static EstadoAprobacion? ToEstadoAprobacion(this string? estado)
        {
            if (string.IsNullOrWhiteSpace(estado))
                return null;

            var normalized = estado.Trim().ToUpperInvariant();

            foreach (EstadoAprobacion value in Enum.GetValues(typeof(EstadoAprobacion)))
            {
                if (value.GetDescription().Equals(normalized, StringComparison.OrdinalIgnoreCase))
                    return value;
            }

            if (Enum.TryParse<EstadoAprobacion>(normalized, true, out var result))
                return result;

            return null;
        }

        public static TipoRequisicion? ToTipoRequisicion(this string? tipo)
        {
            if (string.IsNullOrWhiteSpace(tipo))
                return null;

            var normalized = tipo.Trim().ToUpperInvariant();

            foreach (TipoRequisicion value in Enum.GetValues(typeof(TipoRequisicion)))
            {
                if (value.GetDescription().Equals(normalized, StringComparison.OrdinalIgnoreCase))
                    return value;
            }

            if (Enum.TryParse<TipoRequisicion>(normalized, true, out var result))
                return result;

            return null;
        }

        public static NivelAprobacion? ToNivelAprobacion(this string? nivel)
        {
            if (string.IsNullOrWhiteSpace(nivel))
                return null;

            var normalized = nivel.Trim().ToUpperInvariant();

            foreach (NivelAprobacion value in Enum.GetValues(typeof(NivelAprobacion)))
            {
                if (value.GetDescription().Equals(normalized, StringComparison.OrdinalIgnoreCase))
                    return value;
            }

            // Manejo especial para números
            return normalized switch
            {
                "1" => NivelAprobacion.Nivel1,
                "2" => NivelAprobacion.Nivel2,
                "3" => NivelAprobacion.Nivel3,
                "FINAL" => NivelAprobacion.Final,
                _ => null
            };
        }

        public static int ToNumero(this NivelAprobacion nivel)
        {
            return nivel switch
            {
                NivelAprobacion.Nivel1 => 1,
                NivelAprobacion.Nivel2 => 2,
                NivelAprobacion.Nivel3 => 3,
                NivelAprobacion.Final => 0,
                _ => 0
            };
        }

        public static bool EsRechazado(this EstadoRequisicion estado)
        {
            return estado switch
            {
                EstadoRequisicion.Rechazada => true,
                EstadoRequisicion.RechazadoPorRrhh => true, // Corregido: RechazadoPorGestionGH a RechazadoPorRrhh
                EstadoRequisicion.RechazadoPorSeleccion => true,
                EstadoRequisicion.RechazadoPorNomina => true,
                EstadoRequisicion.RechazadoPorVpGh => true, // Corregido: RechazadoPorVpGH a RechazadoPorVpGh
                _ => false
            };
        }

        public static bool EsEstadoFinal(this EstadoRequisicion estado)
        {
            return estado switch
            {
                EstadoRequisicion.Rechazada => true,
                EstadoRequisicion.RechazadoPorRrhh => true, // Corregido: RechazadoPorGestionGH a RechazadoPorRrhh
                EstadoRequisicion.RechazadoPorSeleccion => true,
                EstadoRequisicion.RechazadoPorNomina => true,
                EstadoRequisicion.RechazadoPorVpGh => true, // Corregido: RechazadoPorVpGH a RechazadoPorVpGh
                EstadoRequisicion.Cerrado => true,
                _ => false
            };
        }

        public static string[] GetEstadosValidos()
        {
            return Enum.GetValues<EstadoRequisicion>()
                .Select(e => e.GetDescription())
                .ToArray();
        }
    }
}
