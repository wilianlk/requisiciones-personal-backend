pipeline {
    agent any

    tools {
        // Usa el SDK de .NET instalado en el servidor Jenkins
        // Si tu versión es .NET 8 cámbialo a dotnet8
        dotnet 'dotnet6'
    }

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
    }

    post {
        success {
            echo '? Build completado con éxito.'
        }
        failure {
            echo '? El build falló. Revisa los logs.'
        }
    }
}
