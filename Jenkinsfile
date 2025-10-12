pipeline  {
    agent any

    stages {
        stage('Clonar repositorio') {
            steps {
                echo 'Repositorio clonado correctamente.'
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
                echo 'Publicación completada exitosamente.'
            }
        }

        stage('Desplegar remoto por SSH') {
            steps {
                echo 'Conectando al servidor remoto KSCSERVER...'
                sshPublisher(publishers: [
                    sshPublisherDesc(
                        configName: 'KSCSERVER',           // Nombre configurado en Publish over SSH
                        transfers: [
                            sshTransfer(
                                sourceFiles: 'publish/**',   // Carpeta local generada por dotnet publish
                                removePrefix: 'publish',     // Evita duplicar la ruta al copiar
                                remoteDirectory: 'C:\\inetpub\\wwwroot\\RequisicionPersonal', // Ruta IIS destino
                                execCommand: 'iisreset'      // Reinicia IIS al finalizar
                            )
                        ],
                        verbose: true
                    )
                ])
                echo '?? Despliegue remoto completado exitosamente.'
            }
        }
    }

    post {
        success {
            echo '? Build y despliegue completados con éxito.'
        }
        failure {
            echo '? El proceso falló. Revisa los logs en la consola de Jenkins.'
        }
    }
}
