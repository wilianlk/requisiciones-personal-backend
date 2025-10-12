pipeline {
    agent any

    stages {
        stage('Clonar repositorio') {
            steps {
                echo '?? Clonando el repositorio...'
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
            }
        }
    }

    post {
        success {
            echo '?? Build y despliegue completados con éxito.'
            emailext (
                subject: "? Despliegue exitoso en KSCSERVER",
                body: """
                <h3>Publicación completada correctamente</h3>
                <p>El proyecto <b>BackendRequisicionPersonal</b> fue compilado y desplegado exitosamente en el servidor <b>KSCSERVER</b>.</p>
                <p><b>Ruta de despliegue:</b> C:\\Users\\admcliente\\Documents\\jenkins_deploy</p>
                <p><b>Fecha y hora:</b> ${new Date()}</p>
                <p>Revisa Jenkins para más detalles del build.</p>
                """,
                to: "wlucumi@recamier.com",
                mimeType: 'text/html'
            )
        }
        failure {
            echo '? El proceso falló. Revisa los logs de Jenkins.'
            emailext (
                subject: "? Fallo en el despliegue de BackendRequisicionPersonal",
                body: """
                <h3>El proceso de publicación falló</h3>
                <p>Revisa la consola de Jenkins para más detalles.</p>
                <p><b>Fecha y hora:</b> ${new Date()}</p>
                """,
                to: "wlucumi@recamier.com",
                mimeType: 'text/html'
            )
        }
    }
}
