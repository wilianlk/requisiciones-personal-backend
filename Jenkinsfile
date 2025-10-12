pipeline {
    agent any

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
                bat 'dotnet publish BackendRequisicionPersonal.csproj -c Release -o ./publish'
                echo '? Publicación completada exitosamente.'
            }
        }

        stage('Desplegar remoto por SSH') {
            steps {
                echo '?? Conectando al servidor remoto KSCSERVER...'
                script {
                    try {
                        sshPublisher(publishers: [
                            sshPublisherDesc(
                                configName: 'KSCSERVER',
                                transfers: [
                                    sshTransfer(
                                        sourceFiles: 'publish/**',
                                        removePrefix: 'publish',
                                        remoteDirectory: 'Documents/jenkins_deploy',
                                        execCommand: ''
                                    )
                                ],
                                verbose: true
                            )
                        ])
                        echo '?? Archivos copiados correctamente al servidor remoto.'
                    } catch (Exception e) {
                        error "? Error durante el despliegue remoto: ${e.message}"
                    }
                }
            }
        }
    }

    post {
        success {
            echo '?? Build y despliegue completados con éxito.'

            // ?? Enviar correo de éxito
            emailext(
                from: 'anticipos@rocket.recamier.com',
                replyTo: 'anticipos@rocket.recamier.com',
                to: 'wlucumi@recamier.com',
                subject: '? Despliegue exitoso en KSCSERVER',
                mimeType: 'text/html',
                body: """
                    <h2 style="color:#28a745;">? Despliegue completado correctamente</h2>
                    <p>El proyecto <b>BackendRequisicionPersonal</b> fue compilado y desplegado exitosamente en el servidor <b>KSCSERVER</b>.</p>
                    <p><b>Ruta de despliegue:</b> C:\\Users\\admcliente\\Documents\\jenkins_deploy</p>
                    <p><b>Fecha y hora:</b> ${new Date()}</p>
                    <hr>
                    <p style="font-size:12px;color:gray;">Mensaje automático enviado por Jenkins CI/CD</p>
                """
            )

            // ?? Notificación y transición en Jira (versión correcta)
            echo '?? Enviando comentario de éxito a Jira...'
            jiraAddComment(
                site: 'Recamier Jira',
                idOrKey: 'AB-12',
                comment: "? Despliegue exitoso del proyecto BackendRequisicionPersonal en KSCSERVER.<br>Build #${env.BUILD_NUMBER}<br>URL: ${env.BUILD_URL}<br>Fecha: ${new Date()}"
            )
            echo '? Comentario agregado correctamente en Jira (AB-12).'

            // ?? Cambio automático de estado
            echo '?? Cambiando estado del issue AB-12...'
            jiraTransitionIssue(
                site: 'Recamier Jira',
                idOrKey: 'AB-12',
                transitionId: '31'  // ?? reemplaza este ID por el correcto (te explico abajo)
            )
            echo '? Estado del issue cambiado correctamente en Jira.'
        }

        failure {
            echo '? El proceso falló. Revisa los logs de Jenkins.'

            // ?? Enviar correo de fallo
            emailext(
                from: 'anticipos@rocket.recamier.com',
                replyTo: 'anticipos@rocket.recamier.com',
                to: 'wlucumi@recamier.com',
                subject: '? Fallo en el despliegue de BackendRequisicionPersonal',
                mimeType: 'text/html',
                body: """
                    <h2 style="color:#dc3545;">? Error durante la publicación</h2>
                    <p>El proceso de build o despliegue no se completó correctamente.</p>
                    <p>Revisa la consola de Jenkins para más detalles del error.</p>
                    <p><b>Fecha y hora:</b> ${new Date()}</p>
                    <hr>
                    <p style="font-size:12px;color:gray;">Mensaje automático enviado por Jenkins CI/CD</p>
                """
            )

            // ?? Notificación de error a Jira
            echo '?? Enviando notificación de error a Jira...'
            jiraAddComment(
                site: 'Recamier Jira',
                idOrKey: 'AB-12',
                comment: "? Fallo en el despliegue del proyecto BackendRequisicionPersonal en KSCSERVER.<br>Build #${env.BUILD_NUMBER}<br>URL: ${env.BUILD_URL}<br>Fecha: ${new Date()}"
            )
            echo '? Comentario de error agregado correctamente en Jira (AB-12).'
        }
    }
}
