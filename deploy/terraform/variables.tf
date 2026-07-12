variable "app_name"    { default = "mediclinic" }
variable "environment" { default = "production" }
variable "aws_region"  { default = "ap-southeast-1" }

variable "github_repository" {
  description = "GitHub repo in org/repo format (for GHCR image path)"
  type        = string
}

variable "image_tag" {
  description = "Docker image tag to deploy"
  type        = string
  default     = "latest"
}

variable "vpc_cidr"              { default = "10.0.0.0/16" }
variable "availability_zones"    { default = ["ap-southeast-1a", "ap-southeast-1b"] }
variable "private_subnet_cidrs"  { default = ["10.0.1.0/24", "10.0.2.0/24"] }
variable "public_subnet_cidrs"   { default = ["10.0.101.0/24", "10.0.102.0/24"] }

variable "db_username" {
  description = "PostgreSQL master username"
  type        = string
  default     = "mediclinic"
}

variable "db_password" {
  description = "PostgreSQL master password"
  type        = string
  sensitive   = true
}

variable "jwt_secret" {
  description = "JWT signing secret (min 32 chars)"
  type        = string
  sensitive   = true
}

variable "rds_instance_class"   { default = "db.t4g.micro" }
variable "rds_allocated_storage" { default = 20 }
variable "elasticache_node_type" { default = "cache.t4g.micro" }

variable "ecs_cpu"           { default = 512 }
variable "ecs_memory"        { default = 1024 }
variable "ecs_desired_count" { default = 1 }
