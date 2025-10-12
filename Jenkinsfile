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

            // ?? Notificación a Jira
            script {
                echo "?? Enviando comentario de éxito a Jira..."
                try {
                    jiraAddComment(
                        site: 'Recamier Jira',
                        issueKey: 'AB-12',
                        comment: "? Despliegue exitoso del proyecto BackendRequisicionPersonal en KSCSERVER.<br>Build #${env.BUILD_NUMBER}<br>URL: ${env.BUILD_URL}<br>Fecha: ${new Date()}"
                    )
                    echo "? Comentario enviado correctamente a Jira (AB-12)."

                    jiraTransitionIssue(
                        site: 'Recamier Jira',
                        issueKey: 'AB-12',
                        transition: [name: 'En pruebas']
                    )
                    echo "?? Estado del issue AB-12 cambiado a 'En pruebas'."
                } catch (err) {
                    echo "? Error al interactuar con Jira: ${err}"
                }
            }
        }

        failure {
            echo '? El proceso falló. Revisa los logs de Jenkins.'
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

            // ?? Notificación a Jira en caso de error
            script {
                echo "?? Enviando comentario de error a Jira..."
                try {
                    jiraAddComment(
                        site: 'Recamier Jira',
                        issueKey: 'AB-12',
                        comment: "? Fallo en el despliegue del proyecto BackendRequisicionPersonal en KSCSERVER.<br>Build #${env.BUILD_NUMBER}<br>URL: ${env.BUILD_URL}<br>Fecha: ${new Date()}"
                    )
                    echo "? Comentario de error enviado correctamente a Jira (AB-12)."
                } catch (err) {
                    echo "? Error al intentar enviar comentario de error a Jira: ${err}"
                }
            }
        }
    }
}
