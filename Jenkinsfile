pipeline {
    agent any

    stages {
        stage('Clonar repositorio') {
            steps {
                echo '?? Clonando el repositorio...'
                // Jenkins realiza automáticamente el clone según la configuración del job
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
                // Fuerza remitente a coincidir con el usuario SMTP
                from: "anticipos@rocket.recamier.com",
                replyTo: "anticipos@rocket.recamier.com",
                headers: [
                    "Reply-To=anticipos@rocket.recamier.com",
                    "Return-Path=anticipos@rocket.recamier.com"
                ],
                subject: "? Despliegue exitoso en KSCSERVER",
                body: """
                    <h2 style="color:#28a745;">? Despliegue completado correctamente</h2>
                    <p>El proyecto <b>BackendRequisicionPersonal</b> fue compilado y desplegado exitosamente en el servidor <b>KSCSERVER</b>.</p>
                    <p><b>Ruta de despliegue:</b> C:\\Users\\admcliente\\Documents\\jenkins_deploy</p>
                    <p><b>Fecha y hora:</b> ${new Date()}</p>
                    <hr>
                    <p style="font-size:12px;color:gray;">Mensaje automático enviado por Jenkins CI/CD</p>
                """,
                to: "wlucumi@recamier.com",
                mimeType: 'text/html'
            )
        }

        failure {
            echo '? El proceso falló. Revisa los logs de Jenkins.'
            emailext(
                from: "anticipos@rocket.recamier.com",
                replyTo: "anticipos@rocket.recamier.com",
                headers: [
                    "Reply-To=anticipos@rocket.recamier.com",
                    "Return-Path=anticipos@rocket.recamier.com"
                ],
                subject: "? Fallo en el despliegue de BackendRequisicionPersonal",
                body: """
                    <h2 style="color:#dc3545;">? Error durante la publicación</h2>
                    <p>El proceso de build o despliegue no se completó correctamente.</p>
                    <p>Revisa la consola de Jenkins para más detalles del error.</p>
                    <p><b>Fecha y hora:</b> ${new Date()}</p>
                    <hr>
                    <p style="font-size:12px;color:gray;">Mensaje automático enviado por Jenkins CI/CD</p>
                """,
                to: "wlucumi@recamier.com",
                mimeType: 'text/html'
            )
        }
    }
}
