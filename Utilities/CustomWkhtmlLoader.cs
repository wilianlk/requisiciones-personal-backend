using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BackendRequisicionPersonal.Utilities
{
    public static class CustomWkhtmlLoader
    {
        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        public static void LoadWkhtmltox()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "libwkhtmltox", "64bit", "libwkhtmltox.dll");

            if (!File.Exists(path))
                throw new FileNotFoundException("No se encontró libwkhtmltox.dll en la ruta esperada", path);

            var ptr = LoadLibrary(path);

            if (ptr == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                throw new Exception($"Error cargando libwkhtmltox.dll (código {err}). Ruta: {path}");
            }
        }
    }
}
