pipeline {
    agent any

    environment {
        ARTIFACT_NAME = "BackendRequisicionPersonal_${env.BUILD_NUMBER}.zip"
    }

    stages {

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

        stage('Publicar artefactos') {
            steps {
                echo '??? Generando artefacto comprimido...'
                bat 'dotnet publish BackendRequisicionPersonal.csproj -c Release -o ./publish'
                bat "powershell Compress-Archive -Path publish\\* -DestinationPath ${env.ARTIFACT_NAME}"
                archiveArtifacts artifacts: "${env.ARTIFACT_NAME}", fingerprint: true
                echo "? Artefacto archivado: ${env.ARTIFACT_NAME}"
            }
        }

        stage('Desplegar remoto por SSH') {
            steps {
                echo '?? Iniciando despliegue remoto en KSCSERVER...'
                script {
                    try {
                        sshPublisher(publishers: [
                            sshPublisherDesc(
                                configName: 'KSCSERVER',
                                transfers: [
                                    sshTransfer(
                                        sourceFiles: "${env.ARTIFACT_NAME}",
                                        removePrefix: '',
                                        remoteDirectory: 'Documents/jenkins_deploy',
                                        execCommand: """
                                            powershell Expand-Archive -Force ${env.ARTIFACT_NAME} .
                                            del ${env.ARTIFACT_NAME}
                                        """
                                    )
                                ],
                                verbose: true
                            )
                        ])
                        echo '?? Despliegue completado correctamente en KSCSERVER.'
                        currentBuild.result = 'SUCCESS'
                    } catch (Exception e) {
                        echo "?? Error durante el despliegue SSH: ${e.message}"
                        currentBuild.result = 'FAILURE'
                    } finally {
                        echo '?? Etapa SSH finalizada.'
                    }
                }
            }
        }
    }

    post {
        always {
            echo '?? Bloque POST ejecutado (éxito o fallo).'
        }

        success {
            echo '?? Build y despliegue completados con éxito.'

            // ?? Envío de notificación por correo
            emailext(
                from: 'anticipos@rocket.recamier.com',
                replyTo: 'anticipos@rocket.recamier.com',
                to: 'wlucumi@recamier.com',
                subject: "? Despliegue exitoso en KSCSERVER (Build #${env.BUILD_NUMBER})",
                mimeType: 'text/html',
                body: """
                    <h2 style="color:#28a745;">? Despliegue completado correctamente</h2>
                    <p>El proyecto <b>BackendRequisicionPersonal</b> fue compilado y desplegado exitosamente en el servidor <b>KSCSERVER</b>.</p>
                    <p><b>Ruta de despliegue:</b> C:\\Users\\admcliente\\Documents\\jenkins_deploy</p>
                    <p><b>Build:</b> #${env.BUILD_NUMBER}</p>
                    <p><b>Fecha y hora:</b> ${new Date()}</p>
                    <hr>
                    <p style="font-size:12px;color:gray;">Mensaje automático enviado por Jenkins CI/CD</p>
                """
            )

            // ?? Notificación a Jira
            script {
                echo '?? Enviando comentario de éxito a Jira...'
                try {
                    jiraAddComment(
                        site: 'Recamier Jira',
                        idOrKey: 'AB-12',
                        comment: "? Despliegue exitoso del proyecto BackendRequisicionPersonal en KSCSERVER.<br><b>Build:</b> #${env.BUILD_NUMBER}<br><b>URL:</b> ${env.BUILD_URL}<br><b>Fecha:</b> ${new Date()}"
                    )
                    echo '? Comentario agregado correctamente en Jira (AB-12).'
                } catch (Exception e) {
                    echo "?? No se pudo enviar el comentario a Jira: ${e.message}"
                }

                // ?? Cambio de estado automático en Jira
                echo '?? Cambiando estado del issue AB-12 a “Pruebas”...'
                try {
                    jiraTransitionIssue(
                        site: 'Recamier Jira',
                        idOrKey: 'AB-12',
                        input: [
                            transition: [
                                id: '42' // ? ID correcto para "Pruebas"
                            ]
                        ]
                    )
                    echo '? Estado del issue cambiado correctamente a “Pruebas”.'
                } catch (Exception e) {
                    echo "?? No se pudo cambiar el estado del issue en Jira: ${e.message}"
                }
            }

            // ?? Limpieza automática de artefactos viejos (mantiene solo los últimos 5)
            script {
                echo '?? Limpiando artefactos antiguos...'
                dir("${env.WORKSPACE}") {
                    bat '''
                        for /f "skip=5 delims=" %%A in ('dir /b /o-d BackendRequisicionPersonal_*.zip 2^>nul') do del "%%A"
                    '''
                }
                echo '?? Limpieza completada.'
            }
        }

        failure {
            echo '? El proceso falló. Revisa los logs de Jenkins.'

            // ?? Notificación de error
            emailext(
                from: 'anticipos@rocket.recamier.com',
                replyTo: 'anticipos@rocket.recamier.com',
                to: 'wlucumi@recamier.com',
                subject: "? Fallo en el despliegue de BackendRequisicionPersonal (Build #${env.BUILD_NUMBER})",
                mimeType: 'text/html',
                body: """
                    <h2 style="color:#dc3545;">? Error durante la publicación</h2>
                    <p>El proceso de build o despliegue no se completó correctamente.</p>
                    <p>Revisa la consola de Jenkins para más detalles del error.</p>
                    <p><b>Build:</b> #${env.BUILD_NUMBER}</p>
                    <p><b>Fecha y hora:</b> ${new Date()}</p>
                    <hr>
                    <p style="font-size:12px;color:gray;">Mensaje automático enviado por Jenkins CI/CD</p>
                """
            )

            // ?? Notificación de error en Jira
            script {
                echo '?? Notificando fallo a Jira...'
                try {
                    jiraAddComment(
                        site: 'Recamier Jira',
                        idOrKey: 'AB-12',
                        comment: "? Fallo en el despliegue del proyecto BackendRequisicionPersonal en KSCSERVER.<br><b>Build:</b> #${env.BUILD_NUMBER}<br><b>URL:</b> ${env.BUILD_URL}<br><b>Fecha:</b> ${new Date()}"
                    )
                    echo '? Comentario de error agregado correctamente en Jira (AB-12).'
                } catch (Exception e) {
                    echo "?? No se pudo notificar el error en Jira: ${e.message}"
                }
            }
        }
    }
}
