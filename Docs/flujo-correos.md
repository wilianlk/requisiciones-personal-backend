# Flujo de envío de correos – BackendRequisicionPersonal

> Nota: por decisión del proyecto, el destinatario "solicitante" que se notifica por correo **se obtiene desde `CorreoJefe`** (no desde `SolicitanteCorreo`).

```mermaid
flowchart TD
  A["Creación de solicitud\nPOST /api/requisiciones/insertar"] --> B["Estado destino: EN REVISIÓN POR GESTIÓN GH\nCorreo a jefe: EnviarCorreoEstadoSolicitanteAsync\nCorreo a GH (botones): EnviarCorreoGhConBotonesAsync\nBotón: GET /api/aprobaciones/revisado-rrhh?id={id}"]

  B --> C["GH marca revisado\nPOST/GET /api/aprobaciones/revisado-rrhh?id={id}"]
  C --> D["Estado destino: EN APROBACIÓN\nCorreo a jefe: NotificarSolicitanteSinDuplicarAprobadoresAsync\nCorreo a aprobador(es): EnviarCorreoAprobadorAsync\nBotón aprobar: GET /api/aprobaciones/accion?...estado=APROBADA\nBotón rechazar: Frontend /rechazar"]

  D --> E{"Aprobador decide"}
  E -->|"RECHAZA"| R1["Estado destino: RECHAZADA\nCorreo final a jefe: EnviarCorreoFinalSolicitanteAsync"]
  E -->|"APRUEBA"| F{"¿Quedan aprobadores?"}

  F -->|"Sí"| D
  F -->|"No"| G["Estado destino: EN SELECCIÓN\nCorreo a jefe: EnviarCorreoEstadoSolicitanteAsync\nCorreo a GH (botones): EnviarCorreoGhConBotonesAsync\nBotón: Frontend /seleccionado?id={id}"]

  G --> H["Se registra el seleccionado\nPOST /api/requisiciones/seleccionado"]
  H --> I["Estado destino: EN NÓMINA\nCorreo a jefe: EnviarCorreoEstadoSolicitanteAsync"]

  I --> J["Nómina decide\nPOST /api/aprobaciones/nomina/accion?accion=APROBADA|RECHAZADA"]
  J --> K{"Acción nómina"}
  K -->|"RECHAZADA"| R2["Estado destino: RECHAZADO POR NOMINA\nCorreo final a jefe: EnviarCorreoFinalSolicitanteAsync\nAviso a GH (si aplica)"]
  K -->|"APROBADA"| L["Estado destino: EN VP GH\nCorreo a VP GH (botones): EnviarCorreoVpGhConBotonesAsync"]

  L --> M{"VP GH decide"}
  M -->|"APRUEBA"| N["POST /api/aprobaciones/vpgh/aprobar\nEstado destino: CERRADO\nCorreos: EnviarCorreoCierreANominaYGhAsync"]
  M -->|"RECHAZA"| R3["POST /api/aprobaciones/vpgh/rechazar\nEstado destino: RECHAZADO POR VP GH\nCorreo final a jefe + aviso a GH"]

  %% Devoluciones
  L --> V1["Devolver a Nómina\nPOST /api/aprobaciones/vpgh/devolver-a-nomina\nEstado destino: EN NÓMINA\nCorreo a jefe + Nómina + GH"]
  I --> V2["Devolver a Selección\nPOST /api/aprobaciones/nomina/devolver-a-seleccion\nEstado destino: EN SELECCIÓN\nCorreo a jefe + GH"]
```

## Nota importante (VP GH botones)
El flujo correcto de VP GH es usar endpoints dedicados (`/api/aprobaciones/vpgh/aprobar` y `/api/aprobaciones/vpgh/rechazar`).
Si algún correo/botón usa `/api/aprobaciones/accion?estado=APROBADO POR VP GH`, debe ajustarse para evitar 404/validaciones.
