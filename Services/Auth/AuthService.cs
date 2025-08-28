using IBM.Data.Db2;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using BackendRequisicionPersonal.Models.Auth;

namespace BackendRequisicionPersonal.Services.Auth
{
    public class AuthService
    {
        private readonly string _cs;

        public AuthService(IConfiguration cfg, IWebHostEnvironment env)
        {
            _cs = env.IsDevelopment()
                ? cfg.GetConnectionString("InformixConnection")
                : cfg.GetConnectionString("InformixConnectionProduction");
        }

        public async Task<UsuarioInfo?> ValidarUsuarioAsync(string identificacion, string correo)
        {
            if (string.IsNullOrWhiteSpace(identificacion) || string.IsNullOrWhiteSpace(correo))
                return null;

            try
            {
                Console.WriteLine($"[AuthService] Validando usuario {identificacion} con correo {correo}");

                const string sql = @"
                    SELECT FIRST 1
                        TRIM(identificacion)                         AS identificacion,
                        TRIM(jefe_inmediato_solicitante)             AS jefeInmediatoSolicitante,
                        TRIM(cargo)                                  AS cargo,
                        TRIM(e_mail)                                 AS correo,
                        TRIM(canal)                                  AS canal,
                        TRIM(area)                                   AS area,
                        TRIM(aprobador1_gerente_canal_o_vp)          AS aprobador1,
                        TRIM(correo_aprobador1)                      AS correoAprobador1,
                        TRIM(aprobador2_gerente_nacional_div)        AS aprobador2,
                        TRIM(correo_aprobador2)                      AS correoAprobador2,
                        TRIM(aprobador3)                             AS aprobador3,
                        TRIM(correo_aprobador3)                      AS correoAprobador3,
                        TRIM(centro_de_costo)                        AS centroCosto,
                        TRIM(vp)                                     AS vp
                    FROM requisiciones_aprobaciones_personal
                    WHERE TRIM(identificacion) = @Identificacion
                      AND UPPER(TRIM(e_mail)) = UPPER(@Correo)";

                UsuarioInfo usuario;

                using (var cn = new DB2Connection(_cs))
                {
                    await cn.OpenAsync();
                    using var cmd = cn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.Parameters.Add(new DB2Parameter("@Identificacion", DB2Type.VarChar) { Value = identificacion });
                    cmd.Parameters.Add(new DB2Parameter("@Correo", DB2Type.VarChar) { Value = correo });

                    using var r = await cmd.ExecuteReaderAsync();
                    if (!await r.ReadAsync())
                    {
                        Console.WriteLine($"[AuthService] Usuario {identificacion} no encontrado o correo inválido");
                        return null;
                    }

                    usuario = new UsuarioInfo
                    {
                        Identificacion = r["identificacion"]?.ToString()?.Trim(),
                        JefeInmediatoSolicitante = r["jefeInmediatoSolicitante"]?.ToString()?.Trim(),
                        Cargo = r["cargo"]?.ToString()?.Trim(),
                        Correo = r["correo"]?.ToString()?.Trim(),
                        Canal = r["canal"]?.ToString()?.Trim(),
                        Area = r["area"]?.ToString()?.Trim(),
                        Aprobador1 = r["aprobador1"]?.ToString()?.Trim(),
                        CorreoAprobador1 = r["correoAprobador1"]?.ToString()?.Trim(),
                        Aprobador2 = r["aprobador2"]?.ToString()?.Trim(),
                        CorreoAprobador2 = r["correoAprobador2"]?.ToString()?.Trim(),
                        Aprobador3 = r["aprobador3"]?.ToString()?.Trim(),
                        CorreoAprobador3 = r["correoAprobador3"]?.ToString()?.Trim(),
                        CentroCosto = r["centroCosto"]?.ToString()?.Trim(),
                        Vp = r["vp"]?.ToString()?.Trim(),
                        Roles = new List<string>()
                    };
                }

                usuario.Roles = await GetRolesAsync(usuario.Identificacion);
                Console.WriteLine($"[AuthService] Usuario {identificacion} validado correctamente con {usuario.Roles.Count} rol(es).");
                return usuario;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Error validando usuario {identificacion}: {ex.Message}");
                return null;
            }
        }

        private async Task<List<string>> GetRolesAsync(string identificacion)
        {
            var roles = new List<string>();

            try
            {
                Console.WriteLine($"[AuthService] Consultando roles para usuario {identificacion}");

                const string sql = @"
                    SELECT r.nombre
                    FROM requisiciones_solicitantes_roles sr
                    JOIN requisiciones_roles r ON r.id = sr.rol_id
                    WHERE sr.solicitante_identificacion = @Identificacion";

                using var cn = new DB2Connection(_cs);
                await cn.OpenAsync();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.Add(new DB2Parameter("@Identificacion", DB2Type.VarChar) { Value = identificacion });

                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    roles.Add(r["nombre"]?.ToString()?.Trim());

                Console.WriteLine($"[AuthService] Roles encontrados para {identificacion}: {roles.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Error consultando roles de {identificacion}: {ex.Message}");
            }

            return roles;
        }
    }
}
