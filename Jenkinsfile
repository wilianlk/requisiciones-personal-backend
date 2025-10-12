pipeline {
    agent any

    stages {
        stage('Clonar repositorio') {
            steps {
                echo '?? Clonando el repositorio...'
                // Jenkins hace el clone automáticamente según la configuración del job
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
                        configName: 'KSCSERVER', // nombre configurado en Publish over SSH
                        transfers: [
                            sshTransfer(
                                sourceFiles: 'publish/**', // publica todo lo generado
                                removePrefix: 'publish',   // evita duplicar la ruta
                                remoteDirectory: '/C:/Users/admcliente/Documents/jenkins_deploy', // carpeta remota
                                execCommand: '' // no reinicia nada, solo copia
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
            echo '?? Build y despliegue completados con éxito en C:\\Users\\admcliente\\Documents\\jenkins_deploy.'
        }
        failure {
            echo '? El proceso falló. Revisa los logs de Jenkins.'
        }
    }
}
