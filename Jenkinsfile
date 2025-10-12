pipeline {
    agent any

    environment {
        ARTIFACT_NAME = "BackendRequisicionPersonal_${BUILD_NUMBER}.zip"
    }

    stages {
        stage('Preparar variables') {
            steps {
                script {
                    env.DATE_TAG      = new Date().format("yyyyMMdd_HHmmss")
                    // OJO: aquí ya NO ponemos "Documents/jenkins_deploy"
                    env.DEPLOY_SUBDIR = "build_${env.BUILD_NUMBER}_${env.DATE_TAG}"
                    echo "?? DEPLOY_SUBDIR = ${env.DEPLOY_SUBDIR}"
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
            steps { bat 'dotnet restore BackendRequisicionPersonal.csproj' }
        }

        stage('Compilar proyecto') {
            steps { bat 'dotnet build BackendRequisicionPersonal.csproj --configuration Release' }
        }

        stage('Publicar artefacto ZIP') {
            steps {
                echo '??? Publicando artefactos...'
                bat 'if exist publish rmdir /s /q publish'
                bat 'dotnet publish BackendRequisicionPersonal.csproj -c Release -o publish'
                bat "powershell -NoProfile -Command \"Compress-Archive -Path publish\\* -DestinationPath ${env.ARTIFACT_NAME} -Force\""
                archiveArtifacts artifacts: "${env.ARTIFACT_NAME}", fingerprint: true
                echo "? Artefacto archivado: ${env.ARTIFACT_NAME}"
            }
        }

        stage('Desplegar en KSCSERVER por SSH') {
            steps {
                echo "?? Desplegando en KSCSERVER (subcarpeta): ${env.DEPLOY_SUBDIR}"
                script {
                    try {
                        sshPublisher(publishers: [
                            sshPublisherDesc(
                                configName: 'KSCSERVER',
                                transfers: [
                                    sshTransfer(
                                        sourceFiles: "${env.ARTIFACT_NAME}",
                                        removePrefix: '',
                                        // ?? IMPORTANTE: solo el subdirectorio
                                        remoteDirectory: "${env.DEPLOY_SUBDIR}",
                                        execCommand: """
                                            powershell -NoProfile -Command "Expand-Archive -Force ${env.ARTIFACT_NAME} . ; Remove-Item -Force ${env.ARTIFACT_NAME}"
                                        """
                                    )
                                ],
                                verbose: true
                            )
                        ])
                        echo "?? Despliegue completado."
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
                    <p><b>Ruta base:</b> C:\\Users\\admcliente\\Documents\\jenkins_deploy</p>
                    <p><b>Subcarpeta del build:</b> ${env.DEPLOY_SUBDIR}</p>
                    <p><b>Build:</b> #${env.BUILD_NUMBER}</p>
                    <p><b>Fecha:</b> ${new Date()}</p>
                """
            )

            // ?? Jira: comentario + transición
            script {
                try {
                    jiraAddComment(
                        site: 'Recamier Jira',
                        idOrKey: 'AB-12',
                        comment: """
                            ? Despliegue exitoso de BackendRequisicionPersonal.<br>
                            ?? Build: #${env.BUILD_NUMBER}<br>
                            ?? Fecha: ${new Date()}<br>
                            ?? Subcarpeta: ${env.DEPLOY_SUBDIR}<br>
                            ?? URL: ${env.BUILD_URL}
                        """
                    )
                    jiraTransitionIssue(
                        site: 'Recamier Jira',
                        idOrKey: 'AB-12',
                        input: [ transition: [ id: '42' ] ]
                    )
                } catch (Exception e) {
                    echo "?? Notificación a Jira con advertencias: ${e.message}"
                }
            }
        }

        failure {
            echo '? El proceso falló. Revisa los logs de Jenkins.'
            emailext(
                from: 'anticipos@rocket.recamier.com',
                to: 'wlucumi@recamier.com',
                subject: "? Fallo en el despliegue (Build #${env.BUILD_NUMBER})",
                mimeType: 'text/html',
                body: """
                    <h2 style='color:#dc3545;'>? Error durante la publicación</h2>
                    <p>Build: #${env.BUILD_NUMBER}</p>
                    <p>Fecha: ${new Date()}</p>
                """
            )
            script {
                try {
                    jiraAddComment(
                        site: 'Recamier Jira',
                        idOrKey: 'AB-12',
                        comment: """
                            ? Fallo en despliegue de BackendRequisicionPersonal.<br>
                            ?? Build: #${env.BUILD_NUMBER}<br>
                            ?? Fecha: ${new Date()}<br>
                            ?? URL: ${env.BUILD_URL}
                        """
                    )
                } catch (Exception e) {
                    echo "?? No se pudo notificar el error en Jira: ${e.message}"
                }
            }
        }
    }
}
