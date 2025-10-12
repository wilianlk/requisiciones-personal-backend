pipeline {
    agent any

    // Puedes dejar solo esto en environment; el resto lo calculo en una etapa
    environment {
        ARTIFACT_NAME = "BackendRequisicionPersonal_${BUILD_NUMBER}.zip"
    }

    stages {

        stage('Preparar variables') {
            steps {
                script {
                    // Fecha legible y ruta de despliegue por build
                    env.DATE_TAG  = new Date().format("yyyyMMdd_HHmmss")
                    env.DEPLOY_DIR = "Documents/jenkins_deploy/build_${env.BUILD_NUMBER}_${env.DATE_TAG}"
                    echo "?? DEPLOY_DIR = ${env.DEPLOY_DIR}"
                    echo "?? ARTIFACT_NAME = ${env.ARTIFACT_NAME}"
                }
            }
        }

        stage('Clonar repositorio') {
            steps {
                echo '?? Clonando el repositorio...'
                checkout scm
            }
        }

        stage('Restaurar dependencias') {
            steps {
                bat 'dotnet restore BackendRequisicionPersonal.csproj'
            }
        }

        stage('Compilar proyecto') {
            steps {
                bat 'dotnet build BackendRequisicionPersonal.csproj --configuration Release'
            }
        }

        stage('Publicar artefacto ZIP') {
            steps {
                echo '??? Publicando artefactos...'
                // Asegura un publish limpio del proyecto
                bat 'if exist publish rmdir /s /q publish'
                bat 'dotnet publish BackendRequisicionPersonal.csproj -c Release -o publish'
                // ZIP plano (sin rutas absolutas)
                bat "powershell -NoProfile -Command \"Compress-Archive -Path publish\\* -DestinationPath ${env.ARTIFACT_NAME} -Force\""
                // Guarda el artefacto en Jenkins (auditoría)
                archiveArtifacts artifacts: "${env.ARTIFACT_NAME}", fingerprint: true
                echo "? Artefacto archivado: ${env.ARTIFACT_NAME}"
            }
        }

        stage('Desplegar en KSCSERVER por SSH') {
            steps {
                echo "?? Desplegando en KSCSERVER -> ${env.DEPLOY_DIR}"
                script {
                    try {
                        sshPublisher(publishers: [
                            sshPublisherDesc(
                                configName: 'KSCSERVER',
                                transfers: [
                                    sshTransfer(
                                        // Enviamos solo el ZIP
                                        sourceFiles: "${env.ARTIFACT_NAME}",
                                        removePrefix: '',
                                        // ?? Creamos carpeta del build y trabajamos DENTRO de ella
                                        remoteDirectory: "${env.DEPLOY_DIR}",
                                        // Descomprime en la carpeta actual y borra el ZIP (sin duplicar rutas)
                                        execCommand: """
                                            powershell -NoProfile -Command "Expand-Archive -Force ${env.ARTIFACT_NAME} . ; Remove-Item -Force ${env.ARTIFACT_NAME}"
                                        """
                                    )
                                ],
                                verbose: true
                            )
                        ])
                        echo "?? Despliegue completado en: ${env.DEPLOY_DIR}"
                        currentBuild.result = 'SUCCESS'
                    } catch (Exception e) {
                        error "? Error en despliegue SSH: ${e.message}"
                    }
                }
            }
        }
    }

    post {

        success {
            echo '?? Build y despliegue completados con éxito.'

            // ?? Correo de éxito
            emailext(
                from: 'anticipos@rocket.recamier.com',
                to: 'wlucumi@recamier.com',
                subject: "? Despliegue exitoso en KSCSERVER (Build #${env.BUILD_NUMBER})",
                mimeType: 'text/html',
                body: """
                    <h2 style='color:#28a745;'>? Despliegue exitoso</h2>
                    <p><b>Proyecto:</b> BackendRequisicionPersonal</p>
                    <p><b>Servidor:</b> KSCSERVER</p>
                    <p><b>Ruta:</b> <code>${env.DEPLOY_DIR}</code></p>
                    <p><b>Build:</b> #${env.BUILD_NUMBER}</p>
                    <p><b>Fecha:</b> ${new Date()}</p>
                    <hr>
                    <small>Mensaje automático Jenkins CI/CD</small>
                """
            )

            // ?? Jira: comentario + transición a "Pruebas" (ID 42)
            script {
                echo '?? Notificando a Jira...'
                try {
                    jiraAddComment(
                        site: 'Recamier Jira',
                        idOrKey: 'AB-12',
                        comment: """
                            ? Despliegue exitoso del proyecto BackendRequisicionPersonal.<br>
                            ?? Build: #${env.BUILD_NUMBER}<br>
                            ?? Fecha: ${new Date()}<br>
                            ?? Ruta: ${env.DEPLOY_DIR}<br>
                            ?? URL: ${env.BUILD_URL}
                        """
                    )
                    echo '?? Comentario agregado en Jira.'
                } catch (Exception e) {
                    echo "?? No se pudo comentar en Jira: ${e.message}"
                }

                try {
                    echo '?? Cambiando estado del issue AB-12 a “Pruebas”...'
                    jiraTransitionIssue(
                        site: 'Recamier Jira',
                        idOrKey: 'AB-12',
                        input: [ transition: [ id: '42' ] ]   // ID confirmado para "Pruebas"
                    )
                    echo '? Estado actualizado en Jira.'
                } catch (Exception e) {
                    echo "?? No se pudo transicionar el issue en Jira: ${e.message}"
                }
            }
        }

        failure {
            echo '? El proceso falló. Revisa los logs de Jenkins.'

            // ?? Correo de error
            emailext(
                from: 'anticipos@rocket.recamier.com',
                to: 'wlucumi@recamier.com',
                subject: "? Fallo en el despliegue (Build #${env.BUILD_NUMBER})",
                mimeType: 'text/html',
                body: """
                    <h2 style='color:#dc3545;'>? Error durante la publicación</h2>
                    <p>El proceso de build o despliegue no se completó correctamente.</p>
                    <p><b>Build:</b> #${env.BUILD_NUMBER}</p>
                    <p><b>Fecha:</b> ${new Date()}</p>
                    <hr>
                    <small>Mensaje automático Jenkins CI/CD</small>
                """
            )

            // ?? Jira: comentario de fallo
            script {
                try {
                    jiraAddComment(
                        site: 'Recamier Jira',
                        idOrKey: 'AB-12',
                        comment: """
                            ? Fallo en el despliegue del proyecto BackendRequisicionPersonal.<br>
                            ?? Fecha: ${new Date()}<br>
                            ?? Build: #${env.BUILD_NUMBER}<br>
                            ?? URL: ${env.BUILD_URL}
                        """
                    )
                    echo '?? Comentario de error agregado en Jira.'
                } catch (Exception e) {
                    echo "?? No se pudo notificar el error en Jira: ${e.message}"
                }
            }
        }
    }
}
