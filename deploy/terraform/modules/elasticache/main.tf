resource "aws_elasticache_subnet_group" "main" {
  name       = "${var.name}-${var.environment}"
  subnet_ids = var.subnet_ids
}

resource "aws_security_group" "redis" {
  name   = "${var.name}-redis-${var.environment}"
  vpc_id = var.vpc_id

  ingress {
    from_port   = 6379
    to_port     = 6379
    protocol    = "tcp"
    cidr_blocks = var.allowed_cidr_blocks
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_elasticache_cluster" "valkey" {
  cluster_id           = "${var.name}-${var.environment}"
  engine               = "valkey"
  engine_version       = "7.2"
  node_type            = var.node_type
  num_cache_nodes      = 1
  parameter_group_name = "default.valkey7"
  subnet_group_name    = aws_elasticache_subnet_group.main.name
  security_group_ids   = [aws_security_group.redis.id]
  tags = { Name = "${var.name}-${var.environment}" }
}

output "endpoint" {
  value = aws_elasticache_cluster.valkey.cache_nodes[0].address
}

variable "name"                {}
variable "environment"         {}
variable "vpc_id"              {}
variable "subnet_ids"          { type = list(string) }
variable "allowed_cidr_blocks" { type = list(string) }
variable "node_type"           { default = "cache.t4g.micro" }
