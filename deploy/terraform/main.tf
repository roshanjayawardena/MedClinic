terraform {
  required_version = ">= 1.9"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  # Remote state — update bucket/key before first apply.
  backend "s3" {
    bucket  = "mediclinic-terraform-state"
    key     = "prod/terraform.tfstate"
    region  = "ap-southeast-1"
    encrypt = true
  }
}

provider "aws" {
  region = var.aws_region
}

# ── VPC ───────────────────────────────────────────────────────────────────────
module "vpc" {
  source = "./modules/vpc"

  name             = var.app_name
  environment      = var.environment
  vpc_cidr         = var.vpc_cidr
  azs              = var.availability_zones
  private_subnets  = var.private_subnet_cidrs
  public_subnets   = var.public_subnet_cidrs
}

# ── RDS PostgreSQL 16 ─────────────────────────────────────────────────────────
module "rds" {
  source = "./modules/rds"

  name               = var.app_name
  environment        = var.environment
  vpc_id             = module.vpc.vpc_id
  subnet_ids         = module.vpc.private_subnet_ids
  allowed_cidr_blocks = [var.vpc_cidr]
  db_name            = "mediclinic"
  db_username        = var.db_username
  db_password        = var.db_password
  instance_class     = var.rds_instance_class
  allocated_storage  = var.rds_allocated_storage
}

# ── ElastiCache Valkey ────────────────────────────────────────────────────────
module "elasticache" {
  source = "./modules/elasticache"

  name               = var.app_name
  environment        = var.environment
  vpc_id             = module.vpc.vpc_id
  subnet_ids         = module.vpc.private_subnet_ids
  allowed_cidr_blocks = [var.vpc_cidr]
  node_type          = var.elasticache_node_type
}

# ── S3 bucket (replaces MinIO in production) ──────────────────────────────────
module "s3" {
  source = "./modules/s3"

  name        = var.app_name
  environment = var.environment
}

# ── IAM role for ECS tasks ────────────────────────────────────────────────────
module "ecs" {
  source = "./modules/ecs"

  name              = var.app_name
  environment       = var.environment
  vpc_id            = module.vpc.vpc_id
  public_subnet_ids = module.vpc.public_subnet_ids
  private_subnet_ids = module.vpc.private_subnet_ids

  # Image from GHCR
  api_image      = "ghcr.io/${var.github_repository}:${var.image_tag}"
  migrator_image = "ghcr.io/${var.github_repository}-migrator:${var.image_tag}"

  # Secrets injected as environment variables
  db_connection_string = "Host=${module.rds.endpoint};Port=5432;Database=mediclinic;Username=${var.db_username};Password=${var.db_password}"
  redis_connection     = "${module.elasticache.endpoint}:6379"
  jwt_secret           = var.jwt_secret
  s3_bucket            = module.s3.bucket_name
  aws_region           = var.aws_region

  cpu    = var.ecs_cpu
  memory = var.ecs_memory
  desired_count = var.ecs_desired_count
}
