pipeline {
    agent any

    stages {
        stage('Clonar repositorio') {
            steps {
                echo '?? Clonando el repositorio...'
                checkout scm
            }
        }

        stage('Compilar y publicar') {
            steps {
                bat 'dotnet restore BackendRequisicionPersonal.csproj'
                bat 'dotnet build BackendRequisicionPersonal.csproj --configuration Release'
                bat 'dotnet publish BackendRequisicionPersonal.csproj -c Release -o ./publish'
                echo '? Proyecto publicado correctamente.'
            }
        }

        stage('Desplegar remoto por SSH') {
            steps {
                echo '?? Conectando al servidor remoto KSCSERVER...'
                script {
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
    }

    post {
        success {
            echo '?? Entrando al bloque POST: ÉXITO detectado.'

            script {
                // ===== Prueba de conexión Jira =====
                echo '?? Verificando conexión con Jira...'
                try {
                    jiraGetIssue(site: 'Recamier Jira', idOrKey: 'AB-12')
                    echo '? Conexión a Jira exitosa.'
                } catch (Exception e) {
                    echo "?? Error al conectar con Jira: ${e.message}"
                }

                // ===== Agregar comentario =====
                echo '?? Intentando enviar comentario a Jira...'
                try {
                    jiraAddComment(
                        site: 'Recamier Jira',
                        issueKey: 'AB-12',
                        comment: "? Despliegue exitoso del proyecto BackendRequisicionPersonal en KSCSERVER.<br>Build #${env.BUILD_NUMBER}<br>URL: ${env.BUILD_URL}<br>Fecha: ${new Date()}"
                    )
                    echo '? Comentario agregado correctamente en Jira (AB-12).'
                } catch (Exception e) {
                    echo "?? No se pudo enviar el comentario a Jira: ${e.message}"
                }

                // ===== Cambiar estado =====
                echo '?? Intentando cambiar estado en Jira...'
                try {
                    jiraTransitionIssue(
                        site: 'Recamier Jira',
                        issueKey: 'AB-12',
                        transition: [name: 'En pruebas']
                    )
                    echo '? Estado cambiado a “En pruebas”.'
                } catch (Exception e) {
                    echo "?? No se pudo cambiar el estado en Jira: ${e.message}"
                }
            }
        }
    }
}
